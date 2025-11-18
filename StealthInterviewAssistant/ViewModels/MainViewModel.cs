using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;
using StealthInterviewAssistant.Services;

namespace StealthInterviewAssistant.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly LiveCaptionsService _liveCaptionsService;
        private GoogleCalendarService? _calendarService;
        
        private string _transcript = string.Empty;
        private bool _excludedFromCapture = true; // Default to excluded (matches Window_Loaded behavior)
        private string _browserUrl = "https://chatgpt.com";
        private string _toggleExcludeButtonContent = "Disable Capture";
        private string _statusMessage = "Ready - Click Start to begin capturing";
        private bool _isCapturing = false;
        private string _startStopButtonIcon = "▶";
        private string _startStopButtonTooltip = "Start Live Captions";
        private Color _statusLightColor = Colors.Gray;
        private bool _isCalendarConnected = false;
        private string _calendarStatusMessage = "Not connected to Google Calendar";
        private ObservableCollection<CalendarEvent> _upcomingEvents = new ObservableCollection<CalendarEvent>();
        private CalendarEvent? _nextInterview;
        private bool _isCalendarPanelVisible = false;

        public MainViewModel()
        {
            _liveCaptionsService = new LiveCaptionsService();
            _liveCaptionsService.OnNewText += OnNewTextReceived;
            
            StartCaptionsCommand = new RelayCommand(StartCaptions);
            CopyDeltaCommand = new RelayCommand(CopyDelta);
            ClearBufferCommand = new RelayCommand(ClearBuffer);
            ToggleExcludeCaptureCommand = new RelayCommand(ToggleExcludeCapture);
            ConnectCalendarCommand = new RelayCommand(ConnectCalendar);
            RefreshCalendarCommand = new RelayCommand(() => _ = RefreshCalendar());
            ToggleCalendarPanelCommand = new RelayCommand(ToggleCalendarPanel);
            
            // Initialize button content based on initial state
            UpdateToggleButtonContent();
            
            // Initialize calendar service if credentials are available
            InitializeCalendarService();
        }

        public string Transcript
        {
            get => _transcript;
            set
            {
                _transcript = value;
                OnPropertyChanged();
            }
        }

        public bool ExcludedFromCapture
        {
            get => _excludedFromCapture;
            set
            {
                _excludedFromCapture = value;
                OnPropertyChanged();
                UpdateToggleButtonContent();
            }
        }

        public string ToggleExcludeButtonContent
        {
            get => _toggleExcludeButtonContent;
            set
            {
                _toggleExcludeButtonContent = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public bool IsCapturing
        {
            get => _isCapturing;
            set
            {
                _isCapturing = value;
                OnPropertyChanged();
            }
        }

        public string StartStopButtonIcon
        {
            get => _startStopButtonIcon;
            set
            {
                _startStopButtonIcon = value;
                OnPropertyChanged();
            }
        }

        public string StartStopButtonTooltip
        {
            get => _startStopButtonTooltip;
            set
            {
                _startStopButtonTooltip = value;
                OnPropertyChanged();
            }
        }

        public Color StatusLightColor
        {
            get => _statusLightColor;
            set
            {
                _statusLightColor = value;
                OnPropertyChanged();
            }
        }

        public string BrowserUrl
        {
            get => _browserUrl;
            set
            {
                _browserUrl = value;
                OnPropertyChanged();
            }
        }

        public ICommand StartCaptionsCommand { get; }
        public ICommand CopyDeltaCommand { get; }
        public ICommand ClearBufferCommand { get; }
        public ICommand ToggleExcludeCaptureCommand { get; }
        public ICommand ConnectCalendarCommand { get; }
        public ICommand RefreshCalendarCommand { get; }
        public ICommand ToggleCalendarPanelCommand { get; }

        public bool IsCalendarConnected
        {
            get => _isCalendarConnected;
            set
            {
                _isCalendarConnected = value;
                OnPropertyChanged();
            }
        }

        public string CalendarStatusMessage
        {
            get => _calendarStatusMessage;
            set
            {
                _calendarStatusMessage = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<CalendarEvent> UpcomingEvents
        {
            get => _upcomingEvents;
            set
            {
                _upcomingEvents = value;
                OnPropertyChanged();
            }
        }

        public CalendarEvent? NextInterview
        {
            get => _nextInterview;
            set
            {
                _nextInterview = value;
                OnPropertyChanged();
            }
        }

        public bool IsCalendarPanelVisible
        {
            get => _isCalendarPanelVisible;
            set
            {
                _isCalendarPanelVisible = value;
                OnPropertyChanged();
            }
        }

        private async void StartCaptions()
        {
            if (_isCapturing)
            {
                // Stop capturing
                _liveCaptionsService.Stop();
                StatusMessage = "Stopped - Click Start to begin capturing";
                IsCapturing = false;
                StartStopButtonIcon = "▶";
                StartStopButtonTooltip = "Start Live Captions";
                StatusLightColor = Colors.Gray;
            }
            else
            {
                // Start capturing
                StatusMessage = "Searching for Live Captions window...";
                IsCapturing = false;
                StatusLightColor = Colors.Orange;
                StartStopButtonIcon = "⏸";
                StartStopButtonTooltip = "Connecting...";
                
                bool success = await _liveCaptionsService.StartAsync();
                if (success)
                {
                    StatusMessage = "✓ Connected to Live Captions - Capturing...";
                    IsCapturing = true;
                    StartStopButtonIcon = "⏹";
                    StartStopButtonTooltip = "Stop Live Captions";
                    StatusLightColor = Colors.LimeGreen;
                }
                else
                {
                    StatusMessage = "✗ Failed to connect to Live Captions. Make sure Live Captions is running (Win+Ctrl+L)";
                    IsCapturing = false;
                    StartStopButtonIcon = "▶";
                    StartStopButtonTooltip = "Start Live Captions";
                    StatusLightColor = Colors.Red;
                }
            }
        }

        private void CopyDelta()
        {
            string delta = GetLastCaptionPart();
            if (!string.IsNullOrWhiteSpace(delta))
            {
                Clipboard.SetText(delta);
                MarkLastCaptionAsSent();
            }
        }

        /// <summary>
        /// Gets the delta from LiveCaptionsService (new text since last send).
        /// Used by both the hotkey handler and Copy Delta button.
        /// Note: Call MarkLastCaptionAsSent() after using the delta.
        /// </summary>
        public string GetDeltaAndAdvance()
        {
            string delta = GetLastCaptionPart();
            if (!string.IsNullOrWhiteSpace(delta))
            {
                MarkLastCaptionAsSent();
            }
            return delta;
        }

        /// <summary>
        /// Gets the last part of the caption (text after last separator, or all text if no separator).
        /// Used to send to WebView.
        /// </summary>
        public string GetLastCaptionPart()
        {
            return _liveCaptionsService.GetLastPart();
        }
        
        public void MarkLastCaptionAsSent()
        {
            _liveCaptionsService.MarkLastPartAsSent();
        }

        private void ClearBuffer()
        {
            _liveCaptionsService.Clear();
            Transcript = string.Empty;
        }

        private void ToggleExcludeCapture()
        {
            ExcludedFromCapture = !ExcludedFromCapture;
            // Update status light color based on exclusion state
            StatusLightColor = ExcludedFromCapture ? Colors.LimeGreen : Colors.Orange;
        }

        private void UpdateToggleButtonContent()
        {
            ToggleExcludeButtonContent = ExcludedFromCapture ? "Disable Capture" : "Enable Capture";
        }

        private void OnNewTextReceived(string newText)
        {
            // Replace transcript with full text in real-time (simpler and eliminates duplicates)
            Application.Current.Dispatcher.Invoke(() =>
            {
                Transcript = newText;
            });
        }

        private void InitializeCalendarService()
        {
            try
            {
                var config = App.Configuration;
                if (config != null)
                {
                    var clientId = config["GoogleCalendar:ClientId"];
                    var clientSecret = config["GoogleCalendar:ClientSecret"];
                    
                    if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
                    {
                        _calendarService = new GoogleCalendarService(clientId, clientSecret);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing calendar service: {ex.Message}");
            }
        }

        private async void ConnectCalendar()
        {
            if (_calendarService == null)
            {
                CalendarStatusMessage = "Calendar service not initialized. Check appsettings.json";
                return;
            }

            try
            {
                CalendarStatusMessage = "Connecting to Google Calendar...";
                bool success = await _calendarService.AuthenticateAsync();
                
                if (success)
                {
                    IsCalendarConnected = true;
                    CalendarStatusMessage = "✓ Connected to Google Calendar";
                    await RefreshCalendar();
                }
                else
                {
                    IsCalendarConnected = false;
                    string errorMsg = _calendarService.LastError ?? "Failed to connect. Please try again.";
                    CalendarStatusMessage = $"✗ {errorMsg}";
                    
                    // Show detailed error in a message box for debugging
                    if (!string.IsNullOrEmpty(_calendarService.LastError))
                    {
                        System.Diagnostics.Debug.WriteLine($"Calendar connection failed: {_calendarService.LastError}");
                    }
                }
            }
            catch (Exception ex)
            {
                IsCalendarConnected = false;
                CalendarStatusMessage = $"✗ Error: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Calendar connection error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task RefreshCalendar()
        {
            if (_calendarService == null || !IsCalendarConnected)
            {
                return;
            }

            try
            {
                CalendarStatusMessage = "Refreshing calendar...";
                var interviews = await _calendarService.GetUpcomingInterviewsAsync(5);
                
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpcomingEvents.Clear();
                    foreach (var interview in interviews)
                    {
                        UpcomingEvents.Add(interview);
                    }
                    
                    NextInterview = interviews.FirstOrDefault();
                    
                    if (interviews.Count > 0)
                    {
                        CalendarStatusMessage = $"✓ Found {interviews.Count} upcoming interview(s)";
                    }
                    else
                    {
                        CalendarStatusMessage = "✓ No upcoming interviews found";
                    }
                });
            }
            catch (Exception ex)
            {
                CalendarStatusMessage = $"✗ Error refreshing: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Calendar refresh error: {ex.Message}");
            }
        }

        private void ToggleCalendarPanel()
        {
            IsCalendarPanelVisible = !IsCalendarPanelVisible;
        }
    }
}

