using System.Globalization;
using LoopbackTranscriber.Models;

namespace LoopbackTranscriber.Services;

public static class SummaryPrompts
{
    private const string FinalSystem = """
Eres un asistente que redacta resumenes DETALLADOS y completos de transcripciones de reuniones y llamadas, con tanto contenido como tendria un buen acta. Responde SOLO en texto plano, con EXACTAMENTE este formato (sin JSON, sin markdown, sin #, sin **):

TITULO: titulo breve del contenido (maximo 8 palabras)
RESUMEN:
Proposito: dos o tres frases que expliquen de que trata la conversacion, quienes intervienen y para que se tuvo.
Puntos clave:
- idea, dato, cifra, decision o acuerdo importante, con su detalle concreto
Temas:
* Nombre corto del tema
- detalle concreto de ese tema: que se dijo, por que y que se decidio
Objetivos:
- lo que alguien quiere conseguir o se compromete a hacer
Proximos pasos:
- lo que se hara despues de esta conversacion (otra reunion, una fecha, un seguimiento)

Reglas:
1. Escribe todo el contenido (el titulo y el texto de cada seccion) en {{LANGUAGE}}, aunque la transcripcion este en otro idioma: traduce lo que haga falta. Las etiquetas (TITULO, RESUMEN, Proposito, Puntos clave, Temas, Objetivos, Proximos pasos) se escriben siempre exactamente asi.
2. Se detallado: recoge TODO lo relevante que se dijo (datos, cifras, nombres, fechas, razones, decisiones, dudas y acuerdos) en vez de quedarte en generalidades. En Puntos clave pon entre 5 y 12 puntos segun lo larga que sea la conversacion, cada uno con su contenido concreto (una o dos frases). Usa SOLO lo que aparece en la transcripcion: no inventes ni supongas nada, y si algo no esta claro, no lo incluyas. Incluye siempre Proposito y Puntos clave. Temas, Objetivos y Proximos pasos: incluyelos siempre que haya al menos un elemento que encaje con su definicion (con uno basta); solo si de verdad no hay ninguno, omite la seccion entera, sin la etiqueta y sin frases tipo "no se mencionan objetivos". Antes de terminar, repasa la transcripcion y comprueba que no te dejas ningun objetivo ni proximo paso que si aparezca.
3. Temas: solo si se tratan varios asuntos distintos o uno con bastante detalle. Cada tema va en su propia linea empezando por "* " y debajo sus detalles, uno por linea empezando por "- ". Cada tema suele llevar entre 2 y 5 detalles; solo un tema muy pequeno puede ir en una sola linea: "* Tema: frase".
4. Objetivos reune tanto lo que alguien quiere lograr como lo que alguien se compromete explicitamente a hacer; no existe una seccion de tareas aparte. Si hay etiqueta de quien habla, ponla delante del punto tal cual aparece en la transcripcion.
5. No repitas una misma idea en Objetivos y en Proximos pasos: elige la que mejor encaje. Una recomendacion, opinion, broma o pregunta retorica no es un objetivo ni un proximo paso.
6. Sintetiza con tus palabras: no copies lineas ni pongas un punto por cada intervencion. Afirma lo que se dijo ("quiere una casa de madera") en vez de "se menciona que..." o "se comenta sobre..." y varia la estructura de las frases.
7. Cada turno de la transcripcion empieza con la etiqueta de quien habla: "{{SELF}}" (o "{{SELF_NAMED}}" si el usuario dio su nombre) para el propio usuario y "{{OTHERS}}" para el resto. Usa solo etiquetas que aparezcan en el texto y solo cuando distinguir aporte algo; no las traduzcas. Si el texto no trae ninguna etiqueta, viene de una unica fuente: no nombres a nadie ni inventes "Hablante 1".
8. Si el texto trae puntos marcados con [OBJETIVO] o [PROXIMO_PASO], usalos para esas secciones sin incluir la marca.
9. No te repitas: cada punto debe aportar informacion distinta a los anteriores. Nunca escribas dos puntos que digan casi lo mismo con otras palabras; si ya lo dijiste, sigue con otra cosa. Es mejor poner menos puntos que repetir.
""";

    private const string ChunkSystem = """
Extraes los puntos mas importantes de UNA parte (parte {{PART}} de {{TOTAL}}) de la transcripcion de una llamada larga. Responde SOLO con una lista completa, cada punto en su propia linea empezando por "- ", en una o dos frases con el dato concreto y con tus propias palabras (agrupa las intervenciones relacionadas; no copies lineas). No omitas datos, cifras, nombres, fechas, razones ni decisiones, ni los distintos temas tratados. Usa solo lo que aparece en el fragmento. Omite saludos y muletillas. No repitas un punto con otras palabras. No uses markdown ni titulos.

Antepon una marca cuando corresponda: "[OBJETIVO] " si alguien expresa un proposito propio ("quiero lograr X") o se compromete explicitamente a hacer algo concreto (en ese caso empieza el texto con la etiqueta real de quien habla, tal cual aparece); "[PROXIMO_PASO] " si se menciona una proxima reunion o fecha, o algo que el grupo hara o revisara despues. Nunca marques una simple opinion, recomendacion o pregunta retorica. Los demas puntos van sin marca.

Cada turno empieza con la etiqueta de quien habla ("{{SELF}}" o "{{SELF_NAMED}}" para el usuario, "{{OTHERS}}" para el resto); si no hay etiquetas, es una unica fuente. Responde en el mismo idioma en el que esta escrito el fragmento.
""";

    private const string GoalsSystem = """
Lees la transcripcion de una conversacion y extraes SOLO los objetivos. Un objetivo es algo que alguien quiere conseguir, o que alguien se compromete explicitamente a hacer. NO son objetivos: opiniones, recomendaciones, habitos o costumbres, descripciones de lo que ya ocurrio, bromas ni preguntas retoricas.

Responde con una lista, cada objetivo en su propia linea empezando por "- ", en una frase, con tus propias palabras y en {{LANGUAGE}} (traduce si hace falta). Usa solo lo que aparece en el texto: no inventes ni supongas nada y no repitas ideas. Si el texto trae etiquetas de quien habla y el objetivo es de alguien concreto, pon esa etiqueta delante tal cual aparece. Si el texto trae puntos marcados con [OBJETIVO], usalos sin incluir la marca.

Si no hay ningun objetivo claro, responde exactamente: NINGUNO
""";

    private const string NextStepsSystem = """
Lees la transcripcion de una conversacion y extraes SOLO los proximos pasos. Un proximo paso es algo concreto que se hara despues de esta conversacion: otra reunion, una fecha, un seguimiento o una accion pendiente que se ha acordado. NO son proximos pasos: opiniones, recomendaciones, habitos o costumbres, descripciones de lo que ya ocurrio, bromas ni preguntas retoricas.

Responde con una lista, cada proximo paso en su propia linea empezando por "- ", en una frase, con tus propias palabras y en {{LANGUAGE}} (traduce si hace falta). Usa solo lo que aparece en el texto: no inventes ni supongas nada y no repitas ideas. Si el texto trae puntos marcados con [PROXIMO_PASO], usalos sin incluir la marca.

Si no hay ningun proximo paso claro, responde exactamente: NINGUNO
""";

    public static string Goals(string transcript, string languageName) => FollowUp(GoalsSystem, transcript, languageName);

    public static string NextSteps(string transcript, string languageName) => FollowUp(NextStepsSystem, transcript, languageName);

    private static string FollowUp(string system, string transcript, string languageName) =>
        "<|im_start|>system\n" +
        system.Replace("{{LANGUAGE}}", languageName) +
        " /no_think<|im_end|>\n" +
        "<|im_start|>user\n" +
        "Transcripcion:\n" + transcript + "\n\n/no_think<|im_end|>\n" +
        "<|im_start|>assistant\n";

    public static string Final(string transcript, string languageName) =>
        "<|im_start|>system\n" +
        SpeakerLabels.ApplyToPrompt(FinalSystem).Replace("{{LANGUAGE}}", languageName) +
        " /no_think<|im_end|>\n" +
        "<|im_start|>user\n" +
        "Transcripcion:\n" + transcript + "\n\n/no_think<|im_end|>\n" +
        "<|im_start|>assistant\n";

    public static string Chunk(string chunkText, int partNumber, int totalParts) =>
        "<|im_start|>system\n" +
        SpeakerLabels.ApplyToPrompt(ChunkSystem).Replace("{{PART}}", partNumber.ToString(CultureInfo.InvariantCulture)).Replace("{{TOTAL}}", totalParts.ToString(CultureInfo.InvariantCulture)) +
        " /no_think<|im_end|>\n" +
        "<|im_start|>user\n" +
        "Fragmento:\n" + chunkText + "\n\n/no_think<|im_end|>\n" +
        "<|im_start|>assistant\n";
}
