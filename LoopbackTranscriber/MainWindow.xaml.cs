using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
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
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            var settingsService = new SettingsService();
            var whisper = new WhisperTranscriptionService();
            var transcriptHistoryService = new TranscriptHistoryService();
            var customAppsService = new CustomAppsService(settingsService);

            _viewModel = new MainViewModel(
                new TargetProcessWatcher(),
                new ProcessLoopbackRecorder(),
                new DeviceLoopbackRecorder(),
                whisper,
                settingsService,
                transcriptHistoryService,
                customAppsService);

            DataContext = _viewModel;

            foreach (var target in _viewModel.Targets)
            {
                AppsNavigationView.MenuItems.Add(CreateTargetMenuItem(target));
            }

            _viewModel.Targets.CollectionChanged += OnTargetsCollectionChanged;

            _viewModel.Start();
            Closed += (_, _) =>
            {
                _viewModel.Targets.CollectionChanged -= OnTargetsCollectionChanged;
                _viewModel.Dispose();
            };
        }

        private void OnTargetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (TargetItemViewModel target in e.NewItems)
                {
                    AppsNavigationView.MenuItems.Add(CreateTargetMenuItem(target));
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
            {
                foreach (TargetItemViewModel target in e.OldItems)
                {
                    var item = AppsNavigationView.MenuItems
                        .OfType<NavigationViewItem>()
                        .FirstOrDefault(i => ReferenceEquals(i.Tag, target));

                    if (item is not null)
                    {
                        AppsNavigationView.MenuItems.Remove(item);
                    }
                }
            }
        }

        private NavigationViewItem CreateTargetMenuItem(TargetItemViewModel target)
        {
            var symbol = target.App.Id == "teams" ? SymbolRegular.PeopleTeam24 : SymbolRegular.AppGeneric24;

            var statusDot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusDot.SetBinding(Shape.FillProperty, new Binding(nameof(TargetItemViewModel.IsRunning))
            {
                Source = target,
                Converter = new BoolToStatusBrushConverter()
            });

            var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            text.SetBinding(TextBlock.TextProperty, new Binding(nameof(TargetItemViewModel.DisplayName))
            {
                Source = target
            });

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(statusDot);
            panel.Children.Add(text);

            var item = new NavigationViewItem
            {
                Tag = target,
                Icon = new SymbolIcon { Symbol = symbol },
                Content = panel
            };

            item.SetBinding(NavigationViewItem.CommandProperty, new Binding(nameof(MainViewModel.SelectTargetCommand))
            {
                Source = _viewModel
            });
            item.CommandParameter = target;

            item.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(MainViewModel.IsRecording))
            {
                Source = _viewModel,
                Converter = new InverseBooleanConverter()
            });

            return item;
        }
    }
}
