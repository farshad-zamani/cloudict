using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Cloudict.Abstractions;
using Cloudict.App.Services;

namespace Cloudict.App.Views
{
    /// <summary>
    /// Asks before Cloudict starts listening to the machine instead of the room, and — when the
    /// machine has no way to carry its own output — explains what to install.
    /// </summary>
    public partial class SystemAudioDialog : Window
    {
        private readonly AudioRoutingStatus _status;

        /// <summary>True when the user agreed and the mode should be switched on.</summary>
        public bool Confirmed { get; private set; }

        public SystemAudioDialog(AudioRoutingStatus status)
        {
            InitializeComponent();

            WindowSizing.FitToWorkArea(this, 660, 620);
            _status = status ?? new AudioRoutingStatus { State = AudioRoutingState.Unsupported };

            Title = Loc.Get("SystemAudio_Title");
            TxtHeader.Text = Loc.Get("SystemAudio_Title");

            switch (_status.State)
            {
                case AudioRoutingState.Ready:
                case AudioRoutingState.Active:
                    // The machine can already do it; all that is left is the warning.
                    TxtBody.Text = Loc.Get("SystemAudio_Warning", _status.CaptureDevice ?? "-");
                    BtnConfirm.Content = Loc.Get("SystemAudio_Enable");
                    break;

                case AudioRoutingState.HelperMissing:
                    TxtBody.Text = Loc.Get("SystemAudio_NeedsHelper", _status.HelperName ?? "-");
                    TxtHelperSteps.Text = Loc.Get("SystemAudio_HelperSteps", _status.HelperName ?? "-");
                    BtnGetHelper.Content = Loc.Get("SystemAudio_GetHelper", _status.HelperName ?? "-");
                    PanelHelper.IsVisible = true;

                    // Nothing to confirm until it is installed; the button re-checks instead.
                    BtnConfirm.Content = Loc.Get("SystemAudio_Recheck");
                    break;

                default:
                    TxtBody.Text = Loc.Get("SystemAudio_Unsupported");
                    BtnConfirm.IsVisible = false;
                    break;
            }
        }

        private void OnGetHelperClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var url = string.IsNullOrWhiteSpace(_status.HelperUrl)
                    ? "https://vb-audio.com/Cable/"
                    : _status.HelperUrl;

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemAudioDialog] could not open the download page: {ex.Message}");
            }
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
    }
}
