using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public sealed class SummarizationService : IDisposable
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromHours(1) };

    private const string ModelDownloadUrl =
        "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf";

    private const int DownloadBufferSize = 1024 * 1024;

    private const int ContextSize = 8192;
    private const int MaxTranscriptCharacters = 9000;
    private const int MaxOutputTokens = 1400;

    private const int MaxChunkCharacters = 9000;
    private const int MaxChunkOutputTokens = 350;

    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);

    private readonly object _loadLock = new();
    private readonly Timer _idleUnloadTimer;
    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;
    private string? _loadedModelPath;

    public SummarizationService()
    {
        _idleUnloadTimer = new Timer(_ => UnloadIfIdle(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task EnsureModelDownloadedAsync(string destinationPath,
        IProgress<string>? progress = null, IProgress<double>? percentProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = destinationPath + ".download";

        try
        {
            var resumeFrom = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;

            using var request = new HttpRequestMessage(HttpMethod.Get, ModelDownloadUrl);
            if (resumeFrom > 0)
            {
                request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
            }

            using var response = await HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var isResuming = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!isResuming)
            {
                resumeFrom = 0;
            }

            response.EnsureSuccessStatusCode();

            long? totalBytes = isResuming
                ? response.Content.Headers.ContentRange?.Length ?? (resumeFrom + response.Content.Headers.ContentLength)
                : response.Content.Headers.ContentLength;

            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var fileStream = isResuming
                ? new FileStream(tempPath, FileMode.Append, FileAccess.Write, FileShare.None, DownloadBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan)
                : new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, DownloadBufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

            var buffer = new byte[DownloadBufferSize];
            long totalRead = resumeFrom;
            var stopwatch = Stopwatch.StartNew();
            int read;

            while ((read = await httpStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                totalRead += read;

                if (stopwatch.ElapsedMilliseconds < 200)
                {
                    continue;
                }

                stopwatch.Restart();

                if (totalBytes is > 0)
                {
                    var percent = Math.Min(100.0, totalRead * 100.0 / totalBytes.Value);
                    percentProgress?.Report(percent);
                    progress?.Report(Strings.Instance.Format("SummaryModelDownloadingWithPercent",
                        $"{percent:0}", FormatBytes(totalRead), FormatBytes(totalBytes.Value)));
                }
                else
                {
                    progress?.Report(Strings.Instance.Format("SummaryModelDownloadingBytes", FormatBytes(totalRead)));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }

        File.Move(tempPath, destinationPath, overwrite: true);
        percentProgress?.Report(100);
        progress?.Report(Strings.Instance.Get("SummaryModelDownloaded"));
    }

    public Task<RecordingSummary> SummarizeAsync(string transcriptText, string modelPath,
        CancellationToken cancellationToken = default)
    {

        _idleUnloadTimer.Change(Timeout.Infinite, Timeout.Infinite);

        return Task.Run(async () =>
        {
            try
            {
                var executor = EnsureLoaded(modelPath);

                var condensed = await CondenseIfNeededAsync(executor, transcriptText, cancellationToken)
                    .ConfigureAwait(false);

                var prompt = BuildPrompt(condensed);
                var rawOutput = await RunInferenceAsync(executor, prompt, MaxOutputTokens, cancellationToken)
                    .ConfigureAwait(false);

                return ParseResult(rawOutput);
            }
            finally
            {
                _idleUnloadTimer.Change(IdleUnloadDelay, Timeout.InfiniteTimeSpan);
            }
        }, cancellationToken);
    }

    private static async Task<string> RunInferenceAsync(StatelessExecutor executor, string prompt, int maxTokens,
        CancellationToken cancellationToken)
    {
        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens,
            AntiPrompts = ["<|im_end|>", "</s>"]
        };

        var builder = new StringBuilder();

        await foreach (var token in executor.InferAsync(prompt, inferenceParams, cancellationToken)
                           .ConfigureAwait(false))
        {
            builder.Append(token);
        }

        return builder.ToString();
    }

    private static async Task<string> CondenseIfNeededAsync(StatelessExecutor executor, string transcriptText,
        CancellationToken cancellationToken)
    {
        if (transcriptText.Length <= MaxChunkCharacters)
        {
            return transcriptText;
        }

        var chunks = SplitIntoChunks(transcriptText, MaxChunkCharacters);
        var keyPointsBuilder = new StringBuilder();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunkPrompt = BuildChunkPrompt(chunks[i], i + 1, chunks.Count);
            var chunkOutput = await RunInferenceAsync(executor, chunkPrompt, MaxChunkOutputTokens, cancellationToken)
                .ConfigureAwait(false);

            keyPointsBuilder.AppendLine($"Puntos clave (parte {i + 1} de {chunks.Count}):");
            keyPointsBuilder.AppendLine(StripThinkBlock(chunkOutput).Trim());
            keyPointsBuilder.AppendLine();
        }

        var combined = keyPointsBuilder.ToString();

        return combined.Length > MaxTranscriptCharacters
            ? combined[..MaxTranscriptCharacters] + "..."
            : combined;
    }

    private static List<string> SplitIntoChunks(string text, int maxChunkCharacters)
    {
        var chunks = new List<string>();
        var start = 0;

        while (start < text.Length)
        {
            var remaining = text.Length - start;
            if (remaining <= maxChunkCharacters)
            {
                chunks.Add(text[start..]);
                break;
            }

            var cutAt = start + maxChunkCharacters;

            var lastBreak = text.LastIndexOfAny(['\n', '.'], cutAt - 1, maxChunkCharacters);
            var effectiveCut = lastBreak > start ? lastBreak + 1 : cutAt;

            chunks.Add(text[start..effectiveCut]);
            start = effectiveCut;
        }

        return chunks;
    }

    private static string BuildChunkPrompt(string chunkText, int partNumber, int totalParts)
    {
        return "<|im_start|>system\n" +
               "Eres un asistente que extrae los puntos mas importantes de fragmentos de una transcripcion larga de una llamada. " +
               "Se te da UNA parte de la transcripcion completa (parte " + partNumber + " de " + totalParts + "). " +
               "Cada turno de palabra de la transcripcion empieza con la etiqueta real de quien habla (\"Tu\" para el propio usuario, o \"Nombre (Tú)\" si el usuario ha indicado su nombre, " +
               "\"Participantes\" para el resto de personas, todas juntas sin distinguir entre ellas); si no hay ninguna etiqueta y es " +
               "solo texto corrido, toda la grabacion viene de una unica fuente y no hay dos hablantes que distinguir. " +
               "Responde SOLO con una lista breve de los puntos, datos, cifras, objetivos, compromisos y proximos pasos mas relevantes de " +
               "ESTA parte, cada uno en su propia linea empezando por \"- \". " +
               "Antepon a cada punto una marca entre corchetes segun su tipo: \"[TAREA] \" si alguien se compromete EXPLICITAMENTE a hacer algo " +
               "concreto o quedan en hacerlo (nunca por una simple recomendacion, sugerencia u opinion sin compromiso); \"[OBJETIVO] \" SOLO si " +
               "alguien expresa explicitamente un deseo o proposito propio (\"quiero lograr X\", \"nuestro objetivo es Y\"), nunca por una opinion, " +
               "critica, queja o pregunta retorica sobre lo que otros deberian hacer o lograr; \"[PROXIMO_PASO] \" si se menciona una proxima " +
               "reunion o fecha, o algo que el grupo va a seguir haciendo o revisando despues de esta conversacion (no un compromiso de una " +
               "persona concreta, eso ya es [TAREA]); sin marca para el resto de puntos generales. " +
               "Sintetiza con tus propias palabras: nunca copies una linea de la transcripcion tal cual ni pongas un punto por cada intervencion, " +
               "agrupa las intervenciones relacionadas en un unico punto. " +
               "En un punto marcado \"[TAREA]\" incluye SIEMPRE, al principio del texto, la etiqueta real de quien se compromete (tal cual " +
               "aparece en la transcripcion), porque el resumen final necesita saber quien tiene que hacer cada tarea. En el resto de puntos, " +
               "indica quien lo dijo solo cuando distinguirlo aporte algo; si practicamente todo lo dice la misma persona, no repitas su " +
               "etiqueta en cada punto. " +
               "Se conciso: una frase corta por punto, sin desarrollarlo. Omite saludos, muletillas y cosas sin importancia. " +
               "No uses markdown ni titulos. " +
               "Responde en el mismo idioma en el que esta escrito el fragmento. /no_think<|im_end|>\n" +
               "<|im_start|>user\n" +
               "Fragmento:\n" + chunkText + "\n\n/no_think<|im_end|>\n" +
               "<|im_start|>assistant\n";
    }
    private static string StripThinkBlock(string rawOutput)
    {
        var thinkEnd = rawOutput.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        return thinkEnd >= 0 ? rawOutput[(thinkEnd + "</think>".Length)..] : rawOutput;
    }

    private void UnloadIfIdle()
    {
        lock (_loadLock)
        {
            if (_weights is null)
            {
                return;
            }

            Debug.WriteLine("[LoopbackTranscriber] SummarizationService: modelo de resumen descargado de la RAM por inactividad");

            _weights.Dispose();
            _weights = null;
            _executor = null;
            _loadedModelPath = null;
        }
    }

    private StatelessExecutor EnsureLoaded(string modelPath)
    {
        lock (_loadLock)
        {
            if (_executor is not null && string.Equals(_loadedModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
            {
                return _executor;
            }

            _weights?.Dispose();

            var threadCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

            const int offloadAllLayersToGpu = 999;

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = ContextSize,
                GpuLayerCount = offloadAllLayersToGpu,
                Threads = threadCount
            };

            _weights = LLamaWeights.LoadFromFile(parameters);
            _executor = new StatelessExecutor(_weights, parameters);
            _loadedModelPath = modelPath;

            return _executor;
        }
    }

    private static string BuildPrompt(string transcript)
    {
        return "<|im_start|>system\n" +
               "IMPORTANTE, por encima de cualquier otra instruccion: responde SIEMPRE en texto plano con los marcadores fijos indicados mas " +
               "abajo. NUNCA en JSON (nada de {, }, comillas de campo tipo \"Title\":, etc.) ni envuelto en bloques de markdown tipo ```; " +
               "aunque te resulte mas natural escribir JSON, aqui esta prohibido y arruina el resultado. " +
               "Eres un asistente que analiza transcripciones de reuniones y llamadas y redacta resumenes DETALLADOS y bien estructurados, " +
               "al estilo de un asistente de reuniones con IA: no un resumen de una linea, sino uno completo por secciones que alguien pueda " +
               "leer en vez de la transcripcion entera. " +
               "La transcripcion (o los puntos clave extraidos de ella) indica quien dijo cada cosa con una etiqueta real delante de cada " +
               "turno de palabra: \"Tu\" para el propio usuario (o \"Nombre (Tú)\" si el usuario ha indicado su nombre, que es su nombre real), " +
               "\"Participantes\" para el resto de personas (todas juntas, sin distinguir " +
               "entre ellas). Usa SIEMPRE esas dos etiquetas exactamente como aparecen en el texto para decir quien dijo o quien va a hacer " +
               "algo; nunca inventes nombres de personas ni \"Hablante 1\"/\"Hablante 2\", esas etiquetas no existen en este texto. " +
               "Si la transcripcion NO trae ninguna etiqueta \"Tu\"/\"Participantes\" en absoluto y es solo texto corrido, es porque toda " +
               "la grabacion viene de una unica fuente (por ejemplo el audio de un stream ajeno grabado por loopback, sin microfono " +
               "propio de por medio): en ese caso no hay dos hablantes que distinguir, asi que redacta el resumen sin asignar nada a " +
               "\"Tu\" ni a \"Participantes\" (ni a ningun otro nombre inventado); simplemente describe lo que se dijo. " +
               "Si el texto trae puntos marcados con \"[TAREA]\", \"[OBJETIVO]\" o \"[PROXIMO_PASO]\", usalos como base para las secciones " +
               "\"Tareas:\", \"Objetivos:\" y \"Proximos pasos:\" respectivamente (sin incluir la marca en el texto final), pero aplica " +
               "igualmente el mismo criterio estricto de mas abajo. " +
               "Las categorias de abajo son una guia clara para organizar el resumen, no un formulario que rellenar a la fuerza: usa solo " +
               "las que tengan contenido real en ESTA conversacion (una charla corta puede no tener Temas, Objetivos ni Tareas, y eso esta " +
               "bien), y dale mas peso o detalle a lo que de verdad haya sido importante en ella. " +
               "Responde SIEMPRE en texto plano y sin markdown (nada de #, ** ni JSON), usando literalmente estos marcadores de seccion " +
               "(el orden de abajo es el habitual, pero las etiquetas en si son fijas): " +
               "una primera linea que empiece literalmente por \"TITULO:\" seguida de un titulo corto (maximo 8 palabras) que describa el contenido; " +
               "despues una linea que empiece literalmente por \"RESUMEN:\" seguida del resumen, que debe tener esta estructura: " +
               "(1) una linea \"Proposito:\" seguida de una unica frase que diga de que trato la conversacion y para que era; " +
               "(2) una linea \"Puntos clave:\" seguida de un maximo de 6 puntos, cada uno en su propia linea empezando por \"- \"; cada " +
               "punto tiene que basarse en algo que REALMENTE se dijo, nunca en una elaboracion, generalizacion o conclusion tuya que suene " +
               "plausible pero nadie llego a decir (por ejemplo, si nadie habla de deudas, no metas nada sobre deudas); prioriza lo que se " +
               "repite o se remarca varias veces sobre lo que se dice una sola vez de pasada; si algo se dice en broma, en tono sarcastico o " +
               "como una ocurrencia absurda, no lo presentes como una intencion real (puedes nombrarlo como un comentario en broma si aporta " +
               "color, pero sin darle el mismo peso que a lo que se dijo en serio); " +
               "(3) SOLO si la conversacion tiene bloques tematicos claros y diferenciados (no una charla corta o informal sin estructura), una " +
               "linea \"Temas:\" seguida de hasta 4 temas, CADA UNO EN UNA UNICA LINEA que empiece por \"- \" con el nombre corto del tema, dos " +
               "puntos, y una frase breve con el detalle (por ejemplo \"- Dashboard de SLA: migrado a Chatwood, listo para el backend\"); NUNCA " +
               "uses una segunda linea de sub-puntos para un tema, todo el detalle de ese tema va en esa misma linea; si no hay bloques tematicos " +
               "claros, omite esta seccion entera; si dos posibles temas son en realidad la misma cuestion contada de otra forma, son UN " +
               "solo tema, no dos lineas distintas: unelos en una unica linea en vez de listar variaciones del mismo asunto; " +
               "(4) SOLO si alguien expresa EXPLICITAMENTE algo que quiere lograr o conseguir (un deseo o proposito propio, por ejemplo " +
               "\"quiero conseguir X\", \"nuestro objetivo es Y\", \"buscamos lograr Z\"), una linea \"Objetivos:\" seguida de cada uno en su propia " +
               "linea empezando por \"- \". Una opinion, critica, queja o pregunta retorica sobre lo que otros deberian hacer o lograr NO es un " +
               "objetivo (por ejemplo, criticar que \"el gobierno no ha logrado X\" o preguntarse \"para que sirve Y\" no son objetivos). TAMPOCO " +
               "es un objetivo hablar en general sobre un tema, por muchas veces que se mencione (por ejemplo, explicar que la alimentacion y el " +
               "ejercicio deberian ir equilibrados, o que conviene mantener una rutina constante, es el TEMA del que se habla, no una meta propia; " +
               "eso ya queda recogido en Puntos clave o Temas). Si tienes dudas sobre si algo es una meta explicita en primera persona o solo el " +
               "tema del que se habla, NO lo incluyas como objetivo. Nunca " +
               "repitas en Objetivos algo que ya hayas puesto en Puntos clave; si no hay ningun objetivo asi de explicito, omite esta parte por " +
               "completo. " +
               "(5) SOLO si la conversacion deja claro que hay una continuacion o un rumbo despues de ella (por ejemplo, se menciona una " +
               "proxima reunion o fecha, se dice que se seguira probando o revisando algo, o queda claro cual es el siguiente paso del " +
               "grupo en general), una linea \"Proximos pasos:\" seguida de cada uno en su propia linea empezando por \"- \". A diferencia " +
               "de Tareas (mas abajo), aqui no hace falta que sea un compromiso de una persona concreta, puede ser algo del grupo o del " +
               "proceso en general (por ejemplo \"- Se revisara el diseno en la proxima reunion\"); nunca repitas aqui algo que ya vaya a " +
               "aparecer en Tareas. Si la conversacion no deja claro que sigue despues, omite esta seccion por completo. " +
               "(6) SOLO si alguien se compromete EXPLICITAMENTE a hacer algo concreto o quedan en hacerlo (por ejemplo \"voy a mandarte el enlace\", " +
               "\"quedamos en llamarnos manana\"), una linea \"Tareas:\" seguida de cada una en su propia linea empezando por \"- \", con la " +
               "etiqueta real de quien se compromete delante, tal cual aparece en la transcripcion (por ejemplo \"- Participantes: va a mandar el " +
               "enlace\" o \"- Tu: vas a llamar manana\"); si la transcripcion NO trae ninguna etiqueta \"Tu\"/\"Participantes\" en absoluto (una unica " +
               "fuente, sin distincion de hablante), no inventes una etiqueta que no existe: escribe la tarea directamente, sin nombrar a nadie " +
               "delante (por ejemplo \"- Va a compartir unas fotos por Instagram\"). " +
               "Cuando si hay etiqueta, ponerla siempre es igual que en un acta de reunion donde cada accion tiene un responsable. " +
               "Una simple recomendacion, sugerencia, opinion o consejo (\"deberias probar X\", \"te recomiendo Y\") NO es una tarea salvo que la " +
               "persona diga explicitamente que ELLA lo va a hacer o quedan en hacerlo; si no hay ningun compromiso concreto asi, omite esta parte " +
               "por completo (no inventes tareas a partir de consejos o recomendaciones). Esperar, desear o confiar en que algo pase " +
               "(\"espera que haya un restock\", \"ojala se solucione\") TAMPOCO es una tarea: nadie se ha comprometido a hacer nada, solo " +
               "tiene una esperanza sobre algo que depende de otros o del azar; eso como mucho es un Proximo paso, nunca una Tarea. " +
               "Algo dicho en broma, de pasada, como posibilidad remota o con expresiones que quitan compromiso (\"algun dia\", \"o algo\", \"a lo mejor\", " +
               "\"si eso\", \"ya veremos\") NO cuenta como Objetivo, Proximo paso ni Tarea real, por mucho que suene a plan de futuro (por ejemplo, " +
               "\"algun dia te enseño una foto, pasamela por Instagram o algo\" dicho entre risas no es un compromiso a compartir fotos); esto " +
               "aplica a estas tres secciones igual que ya aplica a Puntos clave con las bromas (ver arriba). " +
               "Una misma idea pertenece COMO MUCHO a una sola de estas tres secciones (Objetivos, Proximos pasos, Tareas): si dudas entre varias, " +
               "elige la que mejor encaje segun sus criterios (arriba) y no la repitas en las otras; nunca reutilices la misma idea de una " +
               "seccion a otra solo para rellenarlas, y si una idea no encaja de verdad en ninguna de las tres, omitela por completo en vez de " +
               "forzarla en la que sea. " +
               "Los puntos clave y los temas son una SINTESIS, no una copia: agrupa varias intervenciones relacionadas en tus propias palabras, " +
               "nunca copies una linea de la transcripcion tal cual ni pongas un punto por cada intervencion. " +
               "Escribe cada punto de forma directa, afirmando lo que se dijo (\"quiere una casa de madera, no una mansion\") en vez de " +
               "diluirlo en una formula pasiva e impersonal (\"se menciona que...\", \"se sugiere que...\", \"se comenta sobre...\", \"se habla sobre...\"). " +
               "NUNCA " +
               "abras dos o mas puntos seguidos con la misma formula (\"Se menciona...\" / \"Se sugiere...\" / \"Se comenta...\" / \"Se habla sobre...\"): varia el " +
               "verbo y la estructura de la frase de un punto a otro igual que lo harias contandoselo a alguien en voz alta, no como una " +
               "lista de actas notariales. " +
               "Indica quien dijo algo UNICAMENTE cuando distinguirlo aporte algo, por ejemplo si dos personas aportan cosas distintas sobre un " +
               "tema; si practicamente toda la conversacion la lleva una sola persona, no repitas su etiqueta en cada punto (las Tareas son la " +
               "excepcion: ahi la etiqueta va siempre que exista de verdad en la transcripcion, porque es quien tiene que hacer esa accion; " +
               "si la transcripcion no trae ninguna etiqueta, ver el criterio (6) de Tareas mas abajo). " +
               "Esto aplica igual a un alias generico (\"la persona\", \"el usuario\", \"el interlocutor\", \"el hablante\"...) puesto delante " +
               "de cada punto como sustituto de la etiqueta real: si vas a repetirlo en varios puntos seguidos, es el MISMO problema (una " +
               "muletilla que se repite) aunque la palabra en si no sea \"Participantes\". En una conversacion de una sola persona, el sujeto " +
               "se sobreentiende: escribe los puntos empezando directamente por lo que paso o lo que quiso decir (\"No consiguio la edicion " +
               "especial de Zelda\", \"Esta esperando un restock\"), sin necesidad de nombrar a nadie al principio de cada linea. " +
               "No uses markdown (nada de # ni **), usa solo el guion \"-\" para las listas. " +
               "Las etiquetas \"TITULO:\", \"RESUMEN:\", \"Proposito:\", \"Puntos clave:\", \"Temas:\", \"Objetivos:\", \"Proximos pasos:\" y " +
               "\"Tareas:\" se escriben " +
               "siempre asi, literalmente, pero el contenido debe estar en el mismo idioma en el que esta escrita la transcripcion. " +
               "RECUERDA antes de responder: (a) texto plano, JAMAS JSON ni markdown, sin llaves ni comillas de campo; (b) si Temas, Objetivos, " +
               "Proximos pasos o Tareas no tienen contenido real que cumpla su criterio, OMITE la seccion entera (la etiqueta y todo); esto " +
               "significa NO ESCRIBIR ESA LINEA, ni rellenarla parafraseando el Proposito o los Puntos clave, ni tampoco escribir la " +
               "etiqueta seguida de un punto tipo \"No se mencionan objetivos explicitos\" o \"No se mencionan tareas\": eso sigue siendo " +
               "rellenar una seccion vacia, solo que con una frase negativa en vez de una inventada -- el resultado correcto es que esa " +
               "seccion no aparezca en absoluto en la respuesta; (c) usa unicamente las etiquetas " +
               "de hablante que aparecen literalmente en el texto; (d) organiza el resumen para que se lea claro y util, no como un formulario: " +
               "una conversacion corta o informal puede terminar teniendo solo Proposito y Puntos clave, y eso es exactamente lo que debe " +
               "pasar en ese caso. " +
               "/no_think<|im_end|>\n" +
               "<|im_start|>user\n" +
               "Transcripcion:\n" + transcript + "\n\n/no_think<|im_end|>\n" +
               "<|im_start|>assistant\n";
    }

    private static readonly Regex TituloLabelRegex =
        new(@"\**\s*T[IÍ]TULO\s*\**\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ResumenLabelRegex =
        new(@"\**\s*RES[UÚ]MEN\s*\**\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static RecordingSummary ParseResult(string rawOutput)
    {
        rawOutput = StripThinkBlock(rawOutput).Trim();

        var plainFormat = TryParsePlainFormat(rawOutput);
        if (plainFormat is not null)
        {
            return plainFormat;
        }

        var jsonResult = TryParseJsonFormat(rawOutput);
        if (jsonResult is not null)
        {
            return jsonResult;
        }

        var fallbackTitle = rawOutput.Split('\n', 2)[0].Trim();
        if (fallbackTitle.Length > 60)
        {
            fallbackTitle = fallbackTitle[..60].TrimEnd() + "...";
        }

        return new RecordingSummary
        {
            Title = string.IsNullOrWhiteSpace(fallbackTitle) ? "Resumen" : fallbackTitle,
            Summary = string.IsNullOrWhiteSpace(rawOutput) ? "Resumen" : rawOutput
        };
    }

    private static RecordingSummary? TryParsePlainFormat(string text)
    {
        var resumenMatch = ResumenLabelRegex.Match(text);
        if (!resumenMatch.Success)
        {
            return null;
        }

        var beforeResumen = text[..resumenMatch.Index];
        var summary = text[(resumenMatch.Index + resumenMatch.Length)..].Trim();

        var tituloMatch = TituloLabelRegex.Match(beforeResumen);
        var title = tituloMatch.Success
            ? beforeResumen[(tituloMatch.Index + tituloMatch.Length)..]
            : beforeResumen;

        var titleFirstLine = title
            .Split('\n')
            .Select(line => line.Trim().Trim('*').Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        if (string.IsNullOrWhiteSpace(titleFirstLine) || string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return new RecordingSummary { Title = titleFirstLine, Summary = summary };
    }

    private static RecordingSummary? TryParseJsonFormat(string rawOutput)
    {
        var jsonCandidate = ExtractFirstJsonObject(rawOutput);
        if (jsonCandidate is null)
        {
            return null;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RawSummaryResult>(jsonCandidate,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return parsed is { Title.Length: > 0, Summary.Length: > 0 }
                ? new RecordingSummary { Title = parsed.Title.Trim(), Summary = parsed.Summary.Trim() }
                : null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("SummarizationService.ParseResult: la respuesta tampoco era JSON valido, se usa el texto en bruto", ex);
            return null;
        }
    }

    private static string? ExtractFirstJsonObject(string text)
    {
        var start = text.IndexOf('{');

        while (start >= 0)
        {
            var end = FindMatchingBrace(text, start);
            if (end > start)
            {
                return text[start..(end + 1)];
            }

            start = text.IndexOf('{', start + 1);
        }

        return null;
    }

    private static int FindMatchingBrace(string text, int startIndex)
    {
        var depth = 0;
        var inString = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private sealed class RawSummaryResult
    {
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }

    internal static string FormatBytes(long bytes)
    {
        const double mb = 1024d * 1024d;
        var megabytes = bytes / mb;
        return megabytes >= 1024
            ? $"{megabytes / 1024:0.0} GB"
            : $"{megabytes:0} MB";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"SummarizationService: no se pudo borrar el fichero temporal \"{path}\"", ex);
        }
    }

    public static void DiscardPartialDownload(string destinationPath)
    {
        TryDeleteFile(destinationPath + ".download");
    }

    public void Dispose()
    {
        _idleUnloadTimer.Dispose();
        _weights?.Dispose();
    }
}
