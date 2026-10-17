using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using LoopbackTranscriber.Converters;
using LoopbackTranscriber.Services;
using LoopbackTranscriber.ViewModels;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace LoopbackTranscriber
{

    public partial class MainWindow
    {
        private void OnAppLanguageChangedForCalendar(object? sender, EventArgs e) => ApplyCalendarCulture(Strings.Instance.Language);

        private static readonly System.Collections.Generic.Dictionary<string, string> LanguageToCulture = new()
        {
            ["es"] = "es-ES",
            ["en"] = "en-US",
            ["pt"] = "pt-PT",
            ["de"] = "de-DE",
            ["ru"] = "ru-RU",
        };

        private void ApplyCalendarCulture(string language)
        {
            var cultureName = LanguageToCulture.TryGetValue(language, out var mapped) ? mapped : "en-US";

            try
            {
                var culture = new CultureInfo(cultureName);
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = culture;

                Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
            }
            catch (CultureNotFoundException)
            {
            }
        }
    }
}
