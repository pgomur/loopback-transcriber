namespace LoopbackTranscriber.Services;

public sealed class Strings : System.ComponentModel.INotifyPropertyChanged
{
    public static Strings Instance { get; } = new();

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    private string _language = "es";

    public string Language => _language;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (Translations.TryGetValue(key, out var byLanguage))
        {
            if (byLanguage.TryGetValue(_language, out var text))
            {
                return text;
            }

            if (byLanguage.TryGetValue("en", out var fallback))
            {
                return fallback;
            }
        }

        return key;
    }

    public string Format(string key, params object?[] args) => string.Format(Get(key), args);

    public void SetLanguage(string language)
    {
        if (string.Equals(_language, language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _language = language;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        ["NavApplications"] = new() { ["es"] = "Aplicaciones", ["en"] = "Applications", ["pt"] = "Aplicativos", ["de"] = "Anwendungen", ["ru"] = "Приложения" },
        ["NavHistory"] = new() { ["es"] = "Transcripciones", ["en"] = "Transcripts", ["pt"] = "Transcrições", ["de"] = "Transkripte", ["ru"] = "Транскрипты" },
        ["NavConfig"] = new() { ["es"] = "Configuracion", ["en"] = "Settings", ["pt"] = "Config", ["de"] = "Einstellungen", ["ru"] = "Настройки" },
        ["StatusSelectApp"] = new() { ["es"] = "Selecciona una aplicacion para empezar a escuchar.", ["en"] = "Select an application to start listening.", ["pt"] = "Selecione um aplicativo para começar a escutar.", ["de"] = "Wähle eine Anwendung aus, um mit dem Zuhören zu beginnen.", ["ru"] = "Выберите приложение, чтобы начать прослушивание." },
        ["RecordStart"] = new() { ["es"] = "Iniciar grabacion", ["en"] = "Start recording", ["pt"] = "Iniciar gravação", ["de"] = "Aufnahme starten", ["ru"] = "Начать запись" },
        ["RecordStop"] = new() { ["es"] = "Detener grabacion", ["en"] = "Stop recording", ["pt"] = "Parar gravação", ["de"] = "Aufnahme stoppen", ["ru"] = "Остановить запись" },
        ["HistoryOfAppTitle"] = new() { ["es"] = "Historial", ["en"] = "History", ["pt"] = "Histórico", ["de"] = "Verlauf", ["ru"] = "История" },
        ["HistoryListTitle"] = new() { ["es"] = "Historial de apps", ["en"] = "History of apps", ["pt"] = "Histórico de aplicativos", ["de"] = "Verlauf der Apps", ["ru"] = "История приложений" },
        ["Delete"] = new() { ["es"] = "Eliminar", ["en"] = "Delete", ["pt"] = "Excluir", ["de"] = "Löschen", ["ru"] = "Удалить" },
        ["OpenFolder"] = new() { ["es"] = "Ver carpeta", ["en"] = "Open folder", ["pt"] = "Ver pasta", ["de"] = "Ordner öffnen", ["ru"] = "Открыть папку" },
        ["FilterTooltip"] = new() { ["es"] = "Filtrar", ["en"] = "Filter", ["pt"] = "Filtrar", ["de"] = "Filtern", ["ru"] = "Фильтр" },
        ["FilterFromDateLabel"] = new() { ["es"] = "Desde", ["en"] = "From", ["pt"] = "De", ["de"] = "Von", ["ru"] = "С" },
        ["FilterToDateLabel"] = new() { ["es"] = "Hasta", ["en"] = "To", ["pt"] = "Até", ["de"] = "Bis", ["ru"] = "По" },
        ["FilterDurationLabel"] = new() { ["es"] = "Duración mínima (minutos)", ["en"] = "Minimum duration (minutes)", ["pt"] = "Duração mínima (minutos)", ["de"] = "Mindestdauer (Minuten)", ["ru"] = "Минимальная длительность (минуты)" },
        ["FilterMinPlaceholder"] = new() { ["es"] = "Mín", ["en"] = "Min", ["pt"] = "Mín", ["de"] = "Min", ["ru"] = "Мин" },
        ["FilterApplyButton"] = new() { ["es"] = "Aplicar", ["en"] = "Apply", ["pt"] = "Aplicar", ["de"] = "Anwenden", ["ru"] = "Применить" },
        ["FilterClearButton"] = new() { ["es"] = "Limpiar filtros", ["en"] = "Clear filters", ["pt"] = "Limpar filtros", ["de"] = "Filter löschen", ["ru"] = "Сбросить фильтры" },
        ["HistoryTitle"] = new() { ["es"] = "Transcripciones", ["en"] = "Transcripts", ["pt"] = "Transcrições", ["de"] = "Transkripte", ["ru"] = "Транскрипты" },
        ["HistorySubtitle"] = new() { ["es"] = "Todas las transcripciones guardadas, de cualquier aplicacion.", ["en"] = "All saved transcripts, from any application.", ["pt"] = "Todas as transcrições salvas, de qualquer aplicativo.", ["de"] = "Alle gespeicherten Transkripte, von jeder Anwendung.", ["ru"] = "Все сохранённые транскрипты из любых приложений." },
        ["AppsTitle"] = new() { ["es"] = "Aplicaciones", ["en"] = "Applications", ["pt"] = "Aplicativos", ["de"] = "Anwendungen", ["ru"] = "Приложения" },
        ["AppsRefresh"] = new() { ["es"] = "Actualizar", ["en"] = "Refresh", ["pt"] = "Atualizar", ["de"] = "Aktualisieren", ["ru"] = "Обновить" },
        ["AppsAddedHeader"] = new() { ["es"] = "Añadidas para escuchar", ["en"] = "Added for listening", ["pt"] = "Adicionados para escuta", ["de"] = "Zum Zuhören hinzugefügt", ["ru"] = "Добавлены для прослушивания" },
        ["AppsNoneAdded"] = new() { ["es"] = "Todavia no has añadido ninguna aplicacion.", ["en"] = "You haven't added any application yet.", ["pt"] = "Você ainda não adicionou nenhum aplicativo.", ["de"] = "Du hast noch keine Anwendung hinzugefügt.", ["ru"] = "Вы ещё не добавили ни одного приложения." },
        ["RecordBlockedBySummary"] = new() { ["es"] = "No se puede grabar mientras se genera un resumen con IA.", ["en"] = "You can't record while an AI summary is being generated.", ["pt"] = "Não é possível gravar enquanto um resumo com IA está sendo gerado.", ["de"] = "Aufnahme nicht möglich, während eine KI-Zusammenfassung erstellt wird.", ["ru"] = "Нельзя записывать, пока создаётся резюме с помощью ИИ." },
        ["DeleteBlockedBySummaryBody"] = new() { ["es"] = "No se puede eliminar una transcripcion mientras se genera su resumen. Espera a que termine.", ["en"] = "A transcript can't be deleted while its summary is being generated. Wait for it to finish.", ["pt"] = "Não é possível excluir uma transcrição enquanto o resumo dela está sendo gerado. Aguarde a conclusão.", ["de"] = "Ein Transkript kann nicht gelöscht werden, während seine Zusammenfassung erstellt wird. Warte, bis sie fertig ist.", ["ru"] = "Нельзя удалить расшифровку, пока создаётся её резюме. Дождитесь завершения." },
        ["CloseDuringSummaryTitle"] = new() { ["es"] = "Resumen en curso", ["en"] = "Summary in progress", ["pt"] = "Resumo em andamento", ["de"] = "Zusammenfassung läuft", ["ru"] = "Идёт создание резюме" },
        ["CloseDuringSummaryBody"] = new() { ["es"] = "Se esta generando un resumen con IA. Si cierras ahora se perdera. ¿Cerrar de todos modos?", ["en"] = "An AI summary is being generated. If you close now it will be lost. Close anyway?", ["pt"] = "Um resumo com IA está sendo gerado. Se você fechar agora, ele será perdido. Fechar mesmo assim?", ["de"] = "Eine KI-Zusammenfassung wird erstellt. Wenn du jetzt schließt, geht sie verloren. Trotzdem schließen?", ["ru"] = "Создаётся резюме с помощью ИИ. Если закрыть сейчас, оно будет потеряно. Всё равно закрыть?" },
        ["SummaryLabelPurpose"] = new() { ["es"] = "Propósito", ["en"] = "Purpose", ["pt"] = "Propósito", ["de"] = "Zweck", ["ru"] = "Цель" },
        ["SummaryLabelKeyPoints"] = new() { ["es"] = "Puntos clave", ["en"] = "Key points", ["pt"] = "Pontos-chave", ["de"] = "Kernpunkte", ["ru"] = "Ключевые моменты" },
        ["SummaryLabelTopics"] = new() { ["es"] = "Temas", ["en"] = "Topics", ["pt"] = "Temas", ["de"] = "Themen", ["ru"] = "Темы" },
        ["SummaryLabelGoals"] = new() { ["es"] = "Objetivos", ["en"] = "Goals", ["pt"] = "Objetivos", ["de"] = "Ziele", ["ru"] = "Цели" },
        ["SummaryLabelNextSteps"] = new() { ["es"] = "Próximos pasos", ["en"] = "Next steps", ["pt"] = "Próximos passos", ["de"] = "Nächste Schritte", ["ru"] = "Следующие шаги" },
        ["SummaryLabelTasks"] = new() { ["es"] = "Tareas", ["en"] = "Tasks", ["pt"] = "Tarefas", ["de"] = "Aufgaben", ["ru"] = "Задачи" },
        ["AppsRemove"] = new() { ["es"] = "Quitar", ["en"] = "Remove", ["pt"] = "Remover", ["de"] = "Entfernen", ["ru"] = "Убрать" },
        ["AppsDetectedHeader"] = new() { ["es"] = "Procesos detectados con audio", ["en"] = "Detected processes with audio", ["pt"] = "Processos detectados com áudio", ["de"] = "Erkannte Prozesse mit Audio", ["ru"] = "Обнаруженные процессы со звуком" },
        ["AppsNoneDetected"] = new() { ["es"] = "No se ha detectado ninguna aplicacion nueva reproduciendo audio ahora mismo.", ["en"] = "No new application playing audio has been detected right now.", ["pt"] = "Nenhum aplicativo novo reproduzindo áudio foi detectado agora.", ["de"] = "Derzeit wurde keine neue Anwendung erkannt, die Audio wiedergibt.", ["ru"] = "Сейчас не обнаружено ни одного нового приложения, воспроизводящего звук." },
        ["AppsAdd"] = new() { ["es"] = "Añadir", ["en"] = "Add", ["pt"] = "Adicionar", ["de"] = "Hinzufügen", ["ru"] = "Добавить" },
        ["ConfigTitle"] = new() { ["es"] = "Configuracion", ["en"] = "Settings", ["pt"] = "Configurações", ["de"] = "Einstellungen", ["ru"] = "Настройки" },
        ["ConfigGeneralSection"] = new() { ["es"] = "General", ["en"] = "General", ["pt"] = "Geral", ["de"] = "Allgemein", ["ru"] = "Общие" },
        ["ConfigModelsSection"] = new() { ["es"] = "Modelos de IA", ["en"] = "AI models", ["pt"] = "Modelos de IA", ["de"] = "KI-Modelle", ["ru"] = "Модели ИИ" },
        ["ConfigTranscriptionSection"] = new() { ["es"] = "Transcripcion", ["en"] = "Transcription", ["pt"] = "Transcrição", ["de"] = "Transkription", ["ru"] = "Транскрипция" },
        ["ConfigDownloadModel"] = new() { ["es"] = "Descargar modelo de transcripcion", ["en"] = "Download transcription model", ["pt"] = "Baixar modelo de transcricao", ["de"] = "Transkriptionsmodell herunterladen", ["ru"] = "Скачать модель транскрипции" },
        ["ConfigTranscriptionModelName"] = new() { ["es"] = "Modelo: Whisper large-v3-turbo (GGML, q5_1, ~624 MB)", ["en"] = "Model: Whisper large-v3-turbo (GGML, q5_1, ~624 MB)", ["pt"] = "Modelo: Whisper large-v3-turbo (GGML, q5_1, ~624 MB)", ["de"] = "Modell: Whisper large-v3-turbo (GGML, q5_1, ~624 MB)", ["ru"] = "Модель: Whisper large-v3-turbo (GGML, q5_1, ~624 МБ)" },
        ["ConfigTranscriptionModelDescription"] = new() { ["es"] = "Modelo local de reconocimiento de voz (se ejecuta en este equipo, sin conexion a internet).", ["en"] = "Local speech-recognition model (runs on this computer, no internet connection needed).", ["pt"] = "Modelo local de reconhecimento de voz (roda neste computador, sem conexao com a internet).", ["de"] = "Lokales Spracherkennungsmodell (läuft auf diesem Computer, ohne Internetverbindung).", ["ru"] = "Локальная модель распознавания речи (работает на этом компьютере, без интернета)." },
        ["FirstRunTitle"] = new() { ["es"] = "Antes de empezar", ["en"] = "Before you start", ["pt"] = "Antes de comecar", ["de"] = "Bevor du beginnst", ["ru"] = "Перед началом" },
        ["FirstRunSubtitle"] = new() { ["es"] = "Todo se ejecuta en este equipo, sin conexion a internet.", ["en"] = "Everything runs locally, without an internet connection.", ["pt"] = "Tudo roda neste computador, sem conexao com a internet.", ["de"] = "Alles läuft lokal, ohne Internetverbindung.", ["ru"] = "Всё работает локально, без подключения к интернету." },
        ["FirstRunTranscriptionModelTitle"] = new() { ["es"] = "Transcripcion: Whisper large-v3-turbo q5_1", ["en"] = "Transcription: Whisper large-v3-turbo q5_1", ["pt"] = "Transcricao: Whisper large-v3-turbo q5_1", ["de"] = "Transkription: Whisper large-v3-turbo q5_1", ["ru"] = "Транскрипция: Whisper large-v3-turbo q5_1" },
        ["FirstRunTranscriptionModelSize"] = new() { ["es"] = " ~624 MB", ["en"] = " ~624 MB", ["pt"] = " ~624 MB", ["de"] = " ~624 MB", ["ru"] = " ~624 МБ" },
        ["FirstRunSummaryModelTitle"] = new() { ["es"] = "Resumen: Qwen3-4B Q4_K_M", ["en"] = "Summary: Qwen3-4B Q4_K_M", ["pt"] = "Resumo: Qwen3-4B Q4_K_M", ["de"] = "Zusammenfassung: Qwen3-4B Q4_K_M", ["ru"] = "Резюме: Qwen3-4B Q4_K_M" },
        ["FirstRunSummaryModelSize"] = new() { ["es"] = " ~2.5 GB", ["en"] = " ~2.5 GB", ["pt"] = " ~2.5 GB", ["de"] = " ~2.5 GB", ["ru"] = " ~2.5 ГБ" },
        ["FirstRunTranscriptionModelDescription"] = new() { ["es"] = "Convierte el audio de las reuniones en texto.", ["en"] = "Turns the meeting audio into text.", ["pt"] = "Transforma o audio das reunioes em texto.", ["de"] = "Wandelt das Besprechungsaudio in Text um.", ["ru"] = "Преобразует аудио встреч в текст." },
        ["FirstRunSummaryModelDescription"] = new() { ["es"] = "Genera los resumenes de las reuniones.", ["en"] = "Generates the meeting summaries.", ["pt"] = "Gera os resumos das reunioes.", ["de"] = "Erstellt die Besprechungszusammenfassungen.", ["ru"] = "Создаёт резюме встреч." },
        ["FirstRunDownloadModels"] = new() { ["es"] = "Descargar modelos", ["en"] = "Download models", ["pt"] = "Baixar modelos", ["de"] = "Modelle herunterladen", ["ru"] = "Скачать модели" },
        ["FirstRunContinue"] = new() { ["es"] = "Continuar", ["en"] = "Continue", ["pt"] = "Continuar", ["de"] = "Weiter", ["ru"] = "Продолжить" },
        ["FirstRunContinueHint"] = new() { ["es"] = "Descarga los dos modelos para poder continuar.", ["en"] = "Download both models to continue.", ["pt"] = "Baixe os dois modelos para continuar.", ["de"] = "Lade beide Modelle herunter, um fortzufahren.", ["ru"] = "Загрузите обе модели, чтобы продолжить." },
        ["ConfigUse"] = new() { ["es"] = "Usar", ["en"] = "Use", ["pt"] = "Usar", ["de"] = "Verwenden", ["ru"] = "Использовать" },
        ["ConfigFoldersSection"] = new() { ["es"] = "Carpetas y guardado", ["en"] = "Folders and saving", ["pt"] = "Pastas e salvamento", ["de"] = "Ordner und Speichern", ["ru"] = "Папки и сохранение" },
        ["ConfigModelsFolderLabel"] = new() { ["es"] = "Carpeta de modelos", ["en"] = "Models folder", ["pt"] = "Pasta de modelos", ["de"] = "Modellordner", ["ru"] = "Папка моделей" },
        ["ConfigOpen"] = new() { ["es"] = "Abrir", ["en"] = "Open", ["pt"] = "Abrir", ["de"] = "Öffnen", ["ru"] = "Открыть" },
        ["ConfigChange"] = new() { ["es"] = "Cambiar", ["en"] = "Change", ["pt"] = "Alterar", ["de"] = "Ändern", ["ru"] = "Изменить" },
        ["ConfigTranscriptsFolderLabel"] = new() { ["es"] = "Carpeta de transcripciones", ["en"] = "Transcripts folder", ["pt"] = "Pasta de transcrições", ["de"] = "Transkriptordner", ["ru"] = "Папка транскриптов" },
        ["ConfigSavedFilesHint"] = new() {
            ["es"] = "Cada grabacion guarda automaticamente en esta carpeta un resumen en Markdown (.md) y el audio (.wav); no es configurable.",
            ["en"] = "Each recording automatically saves a Markdown summary (.md) and the audio (.wav) to this folder; this is not configurable.",
            ["pt"] = "Cada gravacao salva automaticamente nesta pasta um resumo em Markdown (.md) e o audio (.wav); isso nao e configuravel.",
            ["de"] = "Jede Aufnahme speichert automatisch eine Markdown-Zusammenfassung (.md) und das Audio (.wav) in diesem Ordner; das ist nicht konfigurierbar.",
            ["ru"] = "Каждая запись автоматически сохраняет в эту папку сводку в формате Markdown (.md) и аудио (.wav); это не настраивается."
        },
        ["HistoryIconTxtTooltip"] = new() { ["es"] = "Transcripcion guardada en .txt", ["en"] = "Transcript saved as .txt", ["pt"] = "Transcrição salva em .txt", ["de"] = "Transkript als .txt gespeichert", ["ru"] = "Транскрипт сохранён в .txt" },
        ["HistoryIconMdTooltip"] = new() { ["es"] = "Transcripcion guardada en Markdown (.md)", ["en"] = "Transcript saved as Markdown (.md)", ["pt"] = "Transcrição salva em Markdown (.md)", ["de"] = "Transkript als Markdown (.md) gespeichert", ["ru"] = "Транскрипт сохранён в Markdown (.md)" },
        ["HistoryIconAudioTooltip"] = new() { ["es"] = "Audio guardado (.wav)", ["en"] = "Audio saved (.wav)", ["pt"] = "Audio salvo (.wav)", ["de"] = "Audio gespeichert (.wav)", ["ru"] = "Аудио сохранено (.wav)" },
        ["HistoryAudioOnlyText"] = new() { ["es"] = "Esta grabacion solo tiene el audio guardado, no hay transcripcion de texto disponible.", ["en"] = "This recording only has the audio saved, no text transcript is available.", ["pt"] = "Esta gravação so tem o audio salvo, nao ha transcrição de texto disponivel.", ["de"] = "Für diese Aufnahme wurde nur das Audio gespeichert, es ist kein Text-Transkript verfugbar.", ["ru"] = "Для этой записи сохранено только аудио, текстовый транскрипт недоступен." },
        ["ConfigAppLanguageLabel"] = new() { ["es"] = "Idioma de la aplicacion", ["en"] = "Application language", ["pt"] = "Idioma do aplicativo", ["de"] = "Anwendungssprache", ["ru"] = "Язык приложения" },
        ["ConfigUserNameLabel"] = new() { ["es"] = "Tu nombre", ["en"] = "Your name", ["pt"] = "Seu nome", ["de"] = "Dein Name", ["ru"] = "Ваше имя" },
        ["ConfigUserNamePlaceholder"] = new() { ["es"] = "Escribe tu nombre", ["en"] = "Enter your name", ["pt"] = "Digite seu nome", ["de"] = "Gib deinen Namen ein", ["ru"] = "Введите ваше имя" },
        ["ConfigUserNameHint"] = new() { ["es"] = "Se mostrara en las transcripciones en lugar de \"Tú\", como \"Nombre (Tú)\".", ["en"] = "Shown in transcripts instead of \"You\", as \"Name (You)\".", ["pt"] = "Será exibido nas transcrições no lugar de \"Você\", como \"Nome (Você)\".", ["de"] = "Wird in den Transkripten anstelle von \"Du\" angezeigt, als \"Name (Du)\".", ["ru"] = "Отображается в расшифровках вместо «Вы» в виде «Имя (Вы)»." },
        ["ConfigTranslucentUiLabel"] = new() { ["es"] = "Interfaz traslucida", ["en"] = "Translucent interface", ["pt"] = "Interface translucida", ["de"] = "Transluzente Oberflache", ["ru"] = "Полупрозрачный интерфейс" },
        ["ModelStatusDownloaded"] = new() { ["es"] = "Modelo \"{0}\" ya descargado ({1}).", ["en"] = "Model \"{0}\" already downloaded ({1}).", ["pt"] = "Modelo \"{0}\" já baixado ({1}).", ["de"] = "Modell \"{0}\" bereits heruntergeladen ({1}).", ["ru"] = "Модель «{0}» уже загружена ({1})." },
        ["ModelStatusNotDownloaded"] = new() { ["es"] = "El modelo \"{0}\" todavia no esta descargado.", ["en"] = "Model \"{0}\" has not been downloaded yet.", ["pt"] = "O modelo \"{0}\" ainda não foi baixado.", ["de"] = "Das Modell \"{0}\" wurde noch nicht heruntergeladen.", ["ru"] = "Модель «{0}» ещё не загружена." },
        ["DownloadStatusReady"] = new() { ["es"] = "Modelo listo para usarse.", ["en"] = "Model ready to use.", ["pt"] = "Modelo pronto para uso.", ["de"] = "Modell einsatzbereit.", ["ru"] = "Модель готова к использованию." },
        ["DownloadStatusFailed"] = new() { ["es"] = "No se pudo descargar el modelo.", ["en"] = "The model could not be downloaded.", ["pt"] = "Não foi possível baixar o modelo.", ["de"] = "Das Modell konnte nicht heruntergeladen werden.", ["ru"] = "Не удалось загрузить модель." },
        ["DownloadStatusError"] = new() { ["es"] = "Error al descargar el modelo: {0}", ["en"] = "Error downloading the model: {0}", ["pt"] = "Erro ao baixar o modelo: {0}", ["de"] = "Fehler beim Herunterladen des Modells: {0}", ["ru"] = "Ошибка при загрузке модели: {0}" },
        ["DownloadStatusPaused"] = new() { ["es"] = "Descarga en pausa.", ["en"] = "Download paused.", ["pt"] = "Download pausado.", ["de"] = "Download angehalten.", ["ru"] = "Загрузка приостановлена." },
        ["FirstRunPause"] = new() { ["es"] = "Pausar", ["en"] = "Pause", ["pt"] = "Pausar", ["de"] = "Pausieren", ["ru"] = "Пауза" },
        ["FirstRunResume"] = new() { ["es"] = "Reanudar", ["en"] = "Resume", ["pt"] = "Retomar", ["de"] = "Fortsetzen", ["ru"] = "Продолжить" },
        ["FirstRunCancel"] = new() { ["es"] = "Cancelar", ["en"] = "Cancel", ["pt"] = "Cancelar", ["de"] = "Abbrechen", ["ru"] = "Отмена" },
        ["ErrorTitle"] = new() { ["es"] = "Error", ["en"] = "Error", ["pt"] = "Erro", ["de"] = "Fehler", ["ru"] = "Ошибка" },
        ["StatusDetected"] = new() { ["es"] = "{0} detectado. Pulsa \"{1}\" para empezar.", ["en"] = "{0} detected. Press \"{1}\" to start.", ["pt"] = "{0} detectado. Pressione \"{1}\" para começar.", ["de"] = "{0} erkannt. Drücke \"{1}\", um zu starten.", ["ru"] = "{0} обнаружено. Нажмите «{1}», чтобы начать." },
        ["StatusNotRunning"] = new() { ["es"] = "{0} no parece estar abierto todavia. Abrelo y vuelve a seleccionarlo.", ["en"] = "{0} doesn't seem to be open yet. Open it and select it again.", ["pt"] = "{0} ainda não parece estar aberto. Abra-o e selecione-o novamente.", ["de"] = "{0} scheint noch nicht geöffnet zu sein. Öffne es und wähle es erneut aus.", ["ru"] = "Похоже, {0} ещё не открыто. Откройте его и выберите снова." },
        ["StatusRecordingCancelledAppClosed"] = new() { ["es"] = "Grabacion cancelada: {0} se cerro, no se ha guardado nada.", ["en"] = "Recording cancelled: {0} was closed, nothing was saved.", ["pt"] = "Gravação cancelada: {0} foi fechado, nada foi salvo.", ["de"] = "Aufnahme abgebrochen: {0} wurde geschlossen, es wurde nichts gespeichert.", ["ru"] = "Запись отменена: {0} закрыто, ничего не сохранено." },
        ["StatusOpenAndSelect"] = new() { ["es"] = "Abre la aplicacion y selecciona una que este en ejecucion.", ["en"] = "Open the application and select one that is running.", ["pt"] = "Abra o aplicativo e selecione um que esteja em execução.", ["de"] = "Öffne die Anwendung und wähle eine aus, die läuft.", ["ru"] = "Откройте приложение и выберите то, которое запущено." },
        ["StatusRecordFailed"] = new() { ["es"] = "No se pudo iniciar la grabacion: {0}", ["en"] = "The recording could not be started: {0}", ["pt"] = "Não foi possível iniciar a gravação: {0}", ["de"] = "Die Aufnahme konnte nicht gestartet werden: {0}", ["ru"] = "Не удалось начать запись: {0}" },
        ["StatusListening"] = new() { ["es"] = "Escuchando {0}...", ["en"] = "Listening to {0}...", ["pt"] = "Ouvindo {0}...", ["de"] = "Höre {0} zu...", ["ru"] = "Слушаем {0}..." },
        ["StatusProcessingAudio"] = new() { ["es"] = "Procesando audio...", ["en"] = "Processing audio...", ["pt"] = "Processando áudio...", ["de"] = "Audio wird verarbeitet...", ["ru"] = "Обработка звука..." },
        ["StatusCheckingModel"] = new() { ["es"] = "Comprobando el modelo de transcripcion...", ["en"] = "Checking the transcription model...", ["pt"] = "Verificando o modelo de transcrição...", ["de"] = "Transkriptionsmodell wird überprüft...", ["ru"] = "Проверка модели транскрипции..." },
        ["StatusTranscribingWithPercent"] = new() { ["es"] = "Transcribiendo... {0}%", ["en"] = "Transcribing... {0}%", ["pt"] = "Transcrevendo... {0}%", ["de"] = "Transkribiere... {0}%", ["ru"] = "Транскрибирование... {0}%" },
        ["StatusNoTextDetected"] = new() { ["es"] = "(No se detecto ningun texto en el audio)", ["en"] = "(No text was detected in the audio)", ["pt"] = "(Nenhum texto foi detectado no áudio)", ["de"] = "(Im Audio wurde kein Text erkannt)", ["ru"] = "(В аудио текст не обнаружен)" },
        ["StatusTranscriptReadySaved"] = new() { ["es"] = "Transcripcion lista y guardada", ["en"] = "Transcript ready and saved", ["pt"] = "Transcrição pronta e salva", ["de"] = "Transkript fertig und gespeichert", ["ru"] = "Транскрипт готов и сохранён" },
        ["StatusTranscriptReady"] = new() { ["es"] = "Transcripcion lista.", ["en"] = "Transcript ready.", ["pt"] = "Transcrição pronta.", ["de"] = "Transkript fertig.", ["ru"] = "Транскрипт готов." },
        ["StatusErrorProcessing"] = new() { ["es"] = "Error al procesar la grabacion: {0}", ["en"] = "Error processing the recording: {0}", ["pt"] = "Erro ao processar a gravação: {0}", ["de"] = "Fehler bei der Verarbeitung der Aufnahme: {0}", ["ru"] = "Ошибка обработки записи: {0}" },
        ["ConfirmDeleteTranscript"] = new() { ["es"] = "Eliminar la transcripcion de \"{0}\" del {1}? Esta accion no se puede deshacer.", ["en"] = "Delete the transcript of \"{0}\" from {1}? This action cannot be undone.", ["pt"] = "Excluir a transcrição de \"{0}\" de {1}? Esta ação não pode ser desfeita.", ["de"] = "Transkript von \"{0}\" vom {1} löschen? Diese Aktion kann nicht rückgängig gemacht werden.", ["ru"] = "Удалить транскрипт «{0}» от {1}? Это действие нельзя отменить." },
        ["ConfirmDeleteTranscriptTitle"] = new() { ["es"] = "Eliminar transcripcion", ["en"] = "Delete transcript", ["pt"] = "Excluir transcrição", ["de"] = "Transkript löschen", ["ru"] = "Удалить транскрипт" },
        ["ConfirmDeleteSelectedOne"] = new() { ["es"] = "Eliminar la transcripcion seleccionada? Esta accion no se puede deshacer.", ["en"] = "Delete the selected transcript? This action cannot be undone.", ["pt"] = "Excluir a transcrição selecionada? Esta ação não pode ser desfeita.", ["de"] = "Ausgewähltes Transkript löschen? Diese Aktion kann nicht rückgängig gemacht werden.", ["ru"] = "Удалить выбранный транскрипт? Это действие нельзя отменить." },
        ["ConfirmDeleteSelectedMany"] = new() { ["es"] = "Eliminar las {0} transcripciones seleccionadas? Esta accion no se puede deshacer.", ["en"] = "Delete the {0} selected transcripts? This action cannot be undone.", ["pt"] = "Excluir as {0} transcrições selecionadas? Esta ação não pode ser desfeita.", ["de"] = "Die {0} ausgewählten Transkripte löschen? Diese Aktion kann nicht rückgängig gemacht werden.", ["ru"] = "Удалить {0} выбранных транскрипта(ов)? Это действие нельзя отменить." },
        ["ConfirmDeleteSelectedTitle"] = new() { ["es"] = "Eliminar transcripciones", ["en"] = "Delete transcripts", ["pt"] = "Excluir transcrições", ["de"] = "Transkripte löschen", ["ru"] = "Удалить транскрипты" },
        ["ErrorDeleteTranscriptBody"] = new() { ["es"] = "No se pudo eliminar la transcripcion de \"{0}\" del {1}: {2}", ["en"] = "The transcript of \"{0}\" from {1} could not be deleted: {2}", ["pt"] = "Não foi possível excluir a transcrição de \"{0}\" de {1}: {2}", ["de"] = "Das Transkript von \"{0}\" vom {1} konnte nicht gelöscht werden: {2}", ["ru"] = "Не удалось удалить транскрипт «{0}» от {1}: {2}" },
        ["ProgressDownloadingModel"] = new() { ["es"] = "Descargando modelo de transcripcion...", ["en"] = "Downloading transcription model...", ["pt"] = "Baixando modelo de transcricao...", ["de"] = "Transkriptionsmodell wird heruntergeladen...", ["ru"] = "Загрузка модели транскрипции..." },
        ["ProgressModelDownloaded"] = new() { ["es"] = "Modelo de transcripcion descargado.", ["en"] = "Transcription model downloaded.", ["pt"] = "Modelo de transcricao baixado.", ["de"] = "Transkriptionsmodell heruntergeladen.", ["ru"] = "Модель транскрипции загружена." },
        ["ProgressDownloadingWithPercent"] = new() { ["es"] = "Descargando modelo de transcripcion: {0}% ({1} / {2})", ["en"] = "Downloading transcription model: {0}% ({1} / {2})", ["pt"] = "Baixando modelo de transcricao: {0}% ({1} / {2})", ["de"] = "Transkriptionsmodell wird heruntergeladen: {0}% ({1} / {2})", ["ru"] = "Загрузка модели транскрипции: {0}% ({1} / {2})" },
        ["ProgressDownloadingBytes"] = new() { ["es"] = "Descargando modelo de transcripcion: {0} descargados...", ["en"] = "Downloading transcription model: {0} downloaded...", ["pt"] = "Baixando modelo de transcricao: {0} baixados...", ["de"] = "Transkriptionsmodell wird heruntergeladen: {0} heruntergeladen...", ["ru"] = "Загрузка модели транскрипции: {0} загружено..." },
        ["ConfigSummarySection"] = new() { ["es"] = "Resumen con IA", ["en"] = "AI summary", ["pt"] = "Resumo com IA", ["de"] = "KI-Zusammenfassung", ["ru"] = "ИИ-резюме" },
        ["ConfigSummaryModelLabel"] = new() { ["es"] = "Modelo local para generar titulos y resumenes (se ejecuta en este equipo, sin conexion a internet).", ["en"] = "Local model to generate titles and summaries (runs on this computer, no internet connection needed).", ["pt"] = "Modelo local para gerar titulos e resumos (roda neste computador, sem conexao com a internet).", ["de"] = "Lokales Modell zum Erstellen von Titeln und Zusammenfassungen (läuft auf diesem Computer, ohne Internetverbindung).", ["ru"] = "Локальная модель для создания заголовков и резюме (работает на этом компьютере, без интернета)." },
        ["ConfigDownloadSummaryModel"] = new() { ["es"] = "Descargar modelo de resumen", ["en"] = "Download summary model", ["pt"] = "Baixar modelo de resumo", ["de"] = "Zusammenfassungsmodell herunterladen", ["ru"] = "Скачать модель для резюме" },
        ["SummaryModelStatusDownloaded"] = new() { ["es"] = "Modelo descargado ({0}).", ["en"] = "Model downloaded ({0}).", ["pt"] = "Modelo baixado ({0}).", ["de"] = "Modell heruntergeladen ({0}).", ["ru"] = "Модель загружена ({0})." },
        ["SummaryModelStatusNotDownloaded"] = new() { ["es"] = "Modelo no descargado todavia.", ["en"] = "Model not downloaded yet.", ["pt"] = "Modelo ainda nao baixado.", ["de"] = "Modell noch nicht heruntergeladen.", ["ru"] = "Модель ещё не загружена." },
        ["SummaryModelDownloadingWithPercent"] = new() { ["es"] = "Descargando modelo de resumen: {0}% ({1} / {2})", ["en"] = "Downloading summary model: {0}% ({1} / {2})", ["pt"] = "Baixando modelo de resumo: {0}% ({1} / {2})", ["de"] = "Zusammenfassungsmodell wird heruntergeladen: {0}% ({1} / {2})", ["ru"] = "Загрузка модели резюме: {0}% ({1} / {2})" },
        ["SummaryModelDownloadingBytes"] = new() { ["es"] = "Descargando modelo de resumen: {0} descargados...", ["en"] = "Downloading summary model: {0} downloaded...", ["pt"] = "Baixando modelo de resumo: {0} baixados...", ["de"] = "Zusammenfassungsmodell wird heruntergeladen: {0} heruntergeladen...", ["ru"] = "Загрузка модели резюме: {0} загружено..." },
        ["SummaryModelDownloaded"] = new() { ["es"] = "Modelo de resumen descargado.", ["en"] = "Summary model downloaded.", ["pt"] = "Modelo de resumo baixado.", ["de"] = "Zusammenfassungsmodell heruntergeladen.", ["ru"] = "Модель резюме загружена." },
        ["GenerateSummaryTooltip"] = new() { ["es"] = "Generar titulo y resumen con IA", ["en"] = "Generate title and summary with AI", ["pt"] = "Gerar titulo e resumo com IA", ["de"] = "Titel und Zusammenfassung mit KI erstellen", ["ru"] = "Создать заголовок и резюме с помощью ИИ" },
        ["SummaryNoTextTitle"] = new() { ["es"] = "Sin texto disponible", ["en"] = "No text available", ["pt"] = "Sem texto disponivel", ["de"] = "Kein Text verfügbar", ["ru"] = "Текст недоступен" },
        ["SummaryNoTextBody"] = new() { ["es"] = "Esta grabacion solo tiene audio, no hay texto que resumir.", ["en"] = "This recording only has audio, there is no text to summarize.", ["pt"] = "Esta gravacao so tem audio, nao ha texto para resumir.", ["de"] = "Diese Aufnahme enthält nur Audio, es gibt keinen Text zum Zusammenfassen.", ["ru"] = "В этой записи есть только аудио, текста для резюме нет." },
        ["SummaryModelMissingTitle"] = new() { ["es"] = "Modelo de resumen no descargado", ["en"] = "Summary model not downloaded", ["pt"] = "Modelo de resumo nao baixado", ["de"] = "Zusammenfassungsmodell nicht heruntergeladen", ["ru"] = "Модель резюме не загружена" },
        ["SummaryModelMissingBody"] = new() { ["es"] = "Ve a Configuracion y descarga el modelo de resumen antes de usar esta funcion.", ["en"] = "Go to Settings and download the summary model before using this feature.", ["pt"] = "Va em Configuracoes e baixe o modelo de resumo antes de usar esta funcao.", ["de"] = "Gehe zu Einstellungen und lade das Zusammenfassungsmodell herunter, bevor du diese Funktion nutzt.", ["ru"] = "Перейдите в Настройки и загрузите модель резюме перед использованием этой функции." },
        ["SummaryErrorBody"] = new() { ["es"] = "No se pudo generar el resumen: {0}", ["en"] = "The summary could not be generated: {0}", ["pt"] = "Nao foi possivel gerar o resumo: {0}", ["de"] = "Die Zusammenfassung konnte nicht erstellt werden: {0}", ["ru"] = "Не удалось создать резюме: {0}" },
        ["SummaryPanelHeading"] = new() { ["es"] = "Resumen generado por IA", ["en"] = "AI-generated summary", ["pt"] = "Resumo gerado por IA", ["de"] = "KI-generierte Zusammenfassung", ["ru"] = "Резюме, созданное ИИ" },
        ["ConfigSummaryModelName"] = new() { ["es"] = "Modelo: Qwen3-4B (GGUF, Q4_K_M, ~2.5 GB)", ["en"] = "Model: Qwen3-4B (GGUF, Q4_K_M, ~2.5 GB)", ["pt"] = "Modelo: Qwen3-4B (GGUF, Q4_K_M, ~2.5 GB)", ["de"] = "Modell: Qwen3-4B (GGUF, Q4_K_M, ~2.5 GB)", ["ru"] = "Модель: Qwen3-4B (GGUF, Q4_K_M, ~2.5 ГБ)" },
        ["TabTranscript"] = new() { ["es"] = "Transcripcion", ["en"] = "Transcript", ["pt"] = "Transcricao", ["de"] = "Transkript", ["ru"] = "Транскрипт" },
        ["TabSummary"] = new() { ["es"] = "Resumen IA", ["en"] = "AI summary", ["pt"] = "Resumo IA", ["de"] = "KI-Zusammenfassung", ["ru"] = "ИИ-резюме" },
        ["ClearTranscriptViewerTooltip"] = new() { ["es"] = "Limpiar visor", ["en"] = "Clear viewer", ["pt"] = "Limpar visualizador", ["de"] = "Anzeige leeren", ["ru"] = "Очистить просмотр" },
        ["GenerateSummaryButton"] = new() { ["es"] = "Resumir", ["en"] = "Summarize", ["pt"] = "Resumir", ["de"] = "Zusammenfassen", ["ru"] = "Резюмировать" },
        ["GenerateSummaryButtonBusy"] = new() { ["es"] = "Generando...", ["en"] = "Generating...", ["pt"] = "Gerando...", ["de"] = "Wird erstellt...", ["ru"] = "Создание..." },
        ["RegenerateSummaryButton"] = new() { ["es"] = "Regenerar", ["en"] = "Regenerate", ["pt"] = "Regerar", ["de"] = "Neu erstellen", ["ru"] = "Повторить" },
    };
}
