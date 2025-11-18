using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.Util;
using System.IO;
using System.Windows;

namespace StealthInterviewAssistant.Services
{
    public class GoogleCalendarService
    {
        private const string ApplicationName = "Stealth Interview Assistant";
        private const string CalendarScope = "https://www.googleapis.com/auth/calendar.readonly";
        private static readonly string[] Scopes = { CalendarScope };
        private static readonly string CredentialPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StealthInterviewAssistant",
            "google-calendar-credentials.json");

        private Google.Apis.Calendar.v3.CalendarService? _calendarService;
        private UserCredential? _credential;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private string? _lastError;

        public string? LastError => _lastError;

        public GoogleCalendarService(string clientId, string clientSecret)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        /// <summary>
        /// Authenticates the user with Google Calendar API.
        /// </summary>
        public async Task<bool> AuthenticateAsync()
        {
            _lastError = null;
            
            try
            {
                // Validate credentials
                if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
                {
                    _lastError = "Client ID or Client Secret is missing. Please check appsettings.json";
                    return false;
                }

                if (!_clientId.Contains(".apps.googleusercontent.com"))
                {
                    _lastError = "Invalid Client ID format. Should end with .apps.googleusercontent.com";
                    return false;
                }

                var clientSecrets = new ClientSecrets
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret
                };

                // Create the credential store directory if it doesn't exist
                var credentialDir = Path.GetDirectoryName(CredentialPath);
                if (!string.IsNullOrEmpty(credentialDir) && !Directory.Exists(credentialDir))
                {
                    Directory.CreateDirectory(credentialDir);
                }

                // Request authorization
                try
                {
                    _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                        clientSecrets,
                        Scopes,
                        "user",
                        CancellationToken.None,
                        new FileDataStore(CredentialPath, true));
                }
                catch (TaskCanceledException)
                {
                    _lastError = "Authentication was cancelled by user.";
                    return false;
                }
                catch (Exception authEx)
                {
                    // Check if it's an OAuth access denied error
                    if (authEx.Message.Contains("access_denied") || 
                        authEx.Message.Contains("verification") ||
                        authEx.Message.Contains("403"))
                    {
                        _lastError = "Access blocked: Your app is in 'Testing' mode (this is normal for personal apps).\n\n" +
                                   "To fix this:\n" +
                                   "1. Go to Google Cloud Console → APIs & Services → OAuth consent screen\n" +
                                   "2. Scroll to 'Test users' section\n" +
                                   "3. Click '+ ADD USERS' and add your email: techspire0924@gmail.com\n" +
                                   "4. Save and try connecting again\n\n" +
                                   "Note: Testing mode is perfect for personal use. Only publish if you need others to use it.";
                    }
                    else
                    {
                        _lastError = $"Authorization failed: {authEx.Message}";
                    }
                    return false;
                }

                if (_credential == null)
                {
                    _lastError = "Authorization failed: No credential returned. Please check your Google Cloud Console settings.";
                    return false;
                }

                // Check if token is stale and refresh if needed
                if (_credential.Token != null && _credential.Token.IsStale)
                {
                    try
                    {
                        bool refreshed = await _credential.RefreshTokenAsync(CancellationToken.None);
                        if (!refreshed)
                        {
                            _lastError = "Token refresh failed. Please reconnect.";
                            return false;
                        }
                    }
                    catch (Exception refreshEx)
                    {
                        _lastError = $"Token refresh error: {refreshEx.Message}. Please reconnect.";
                        return false;
                    }
                }

                // Create Calendar API service
                _calendarService = new Google.Apis.Calendar.v3.CalendarService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = _credential,
                    ApplicationName = ApplicationName,
                });

                return true;
            }
            catch (GoogleApiException apiEx)
            {
                string errorDetails = $"Google API Error: {apiEx.Message}. Status: {apiEx.HttpStatusCode}.";
                
                // Check for specific OAuth errors
                if (apiEx.HttpStatusCode == System.Net.HttpStatusCode.Forbidden || 
                    apiEx.Message.Contains("access_denied") ||
                    apiEx.Message.Contains("verification"))
                {
                    errorDetails += "\n\nYour app is in 'Testing' mode (normal for personal apps). To fix this:\n" +
                                   "1. Go to Google Cloud Console → APIs & Services → OAuth consent screen\n" +
                                   "2. Scroll to 'Test users' section\n" +
                                   "3. Click '+ ADD USERS' and add your email address\n" +
                                   "4. Try connecting again\n\n" +
                                   "Note: Testing mode is fine for personal use. Only publish if you need public access.";
                }
                else if (apiEx.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    errorDetails += "\n\nMake sure the Calendar API is enabled in Google Cloud Console.";
                }
                
                _lastError = errorDetails;
                System.Diagnostics.Debug.WriteLine($"Calendar API error: {apiEx}");
                return false;
            }
            catch (System.Net.Http.HttpRequestException httpEx)
            {
                _lastError = $"Network error: {httpEx.Message}. Check your internet connection.";
                System.Diagnostics.Debug.WriteLine($"Network error: {httpEx}");
                return false;
            }
            catch (TaskCanceledException)
            {
                _lastError = "Authentication was cancelled. Please try again.";
                return false;
            }
            catch (Exception ex)
            {
                _lastError = $"Authentication failed: {ex.Message}. " +
                            $"Inner: {ex.InnerException?.Message ?? "None"}. " +
                            $"Make sure:\n" +
                            $"1. Redirect URI is configured in Google Cloud Console\n" +
                            $"2. Calendar API is enabled\n" +
                            $"3. OAuth consent screen is configured";
                System.Diagnostics.Debug.WriteLine($"Calendar authentication error: {ex}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Gets list of all calendars the user has access to.
        /// </summary>
        public async Task<List<CalendarListEntry>> GetCalendarsAsync()
        {
            var calendars = new List<CalendarListEntry>();

            if (_calendarService == null)
            {
                bool authenticated = await AuthenticateAsync();
                if (!authenticated)
                {
                    return calendars;
                }
            }

            try
            {
                var request = _calendarService!.CalendarList.List();
                request.MinAccessRole = CalendarListResource.ListRequest.MinAccessRoleEnum.Reader;
                
                var response = await request.ExecuteAsync();

                if (response.Items != null)
                {
                    calendars.AddRange(response.Items.Where(c => c.AccessRole != "freeBusyReader"));
                }
            }
            catch (GoogleApiException apiEx)
            {
                _lastError = $"API Error fetching calendars: {apiEx.Message}";
                System.Diagnostics.Debug.WriteLine($"Calendar API error: {apiEx}");
            }
            catch (Exception ex)
            {
                _lastError = $"Error fetching calendars: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error fetching calendars: {ex.Message}");
            }

            return calendars;
        }

        /// <summary>
        /// Gets upcoming events from Google Calendar.
        /// </summary>
        public async Task<List<CalendarEvent>> GetUpcomingEventsAsync(int maxResults = 10)
        {
            var allEvents = new List<CalendarEvent>();

            if (_calendarService == null)
            {
                bool authenticated = await AuthenticateAsync();
                if (!authenticated)
                {
                    return allEvents;
                }
            }

            try
            {
                // Get all calendars
                var calendars = await GetCalendarsAsync();
                
                if (calendars.Count == 0)
                {
                    // Fallback to primary if no calendars found
                    calendars.Add(new CalendarListEntry { Id = "primary", Summary = "Primary" });
                }

                // Query events from all calendars in parallel
                var tasks = calendars.Select(async calendar =>
                {
                    var calendarEvents = new List<CalendarEvent>();
                    try
                    {
                        var request = _calendarService!.Events.List(calendar.Id);
                        request.TimeMinDateTimeOffset = DateTimeOffset.Now;
                        request.ShowDeleted = false;
                        request.SingleEvents = true;
                        request.MaxResults = maxResults * 2; // Get more per calendar to account for filtering
                        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

                        var response = await request.ExecuteAsync();

                        if (response.Items != null)
                        {
                            foreach (var eventItem in response.Items)
                            {
                                DateTime startTime = DateTime.MinValue;
                                DateTime endTime = DateTime.MinValue;
                                bool isAllDay = false;

                                if (eventItem.Start != null)
                                {
                                    if (eventItem.Start.DateTimeDateTimeOffset.HasValue)
                                    {
                                        startTime = eventItem.Start.DateTimeDateTimeOffset.Value.DateTime;
                                        isAllDay = false;
                                    }
                                    else if (eventItem.Start.Date != null)
                                    {
                                        startTime = DateTime.Parse(eventItem.Start.Date);
                                        isAllDay = true;
                                    }
                                }

                                if (eventItem.End != null)
                                {
                                    if (eventItem.End.DateTimeDateTimeOffset.HasValue)
                                    {
                                        endTime = eventItem.End.DateTimeDateTimeOffset.Value.DateTime;
                                    }
                                    else if (eventItem.End.Date != null)
                                    {
                                        endTime = DateTime.Parse(eventItem.End.Date);
                                    }
                                }

                                var calendarEvent = new CalendarEvent
                                {
                                    Id = eventItem.Id,
                                    Summary = eventItem.Summary ?? "No Title",
                                    Description = eventItem.Description ?? "",
                                    StartTime = startTime,
                                    EndTime = endTime,
                                    Location = eventItem.Location ?? "",
                                    HangoutLink = eventItem.HangoutLink ?? "",
                                    IsAllDay = isAllDay,
                                    CalendarName = calendar.Summary ?? calendar.Id
                                };

                                calendarEvents.Add(calendarEvent);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error fetching events from calendar {calendar.Summary}: {ex.Message}");
                    }
                    return calendarEvents;
                });

                // Wait for all calendar queries to complete
                var results = await Task.WhenAll(tasks);
                
                // Flatten all events from all calendars, then sort by start time
                allEvents = results
                    .SelectMany(events => events)
                    .OrderBy(e => e.StartTime)
                    .Take(maxResults)
                    .ToList();
            }
            catch (GoogleApiException apiEx)
            {
                _lastError = $"API Error fetching events: {apiEx.Message}";
                System.Diagnostics.Debug.WriteLine($"Calendar API error: {apiEx}");
            }
            catch (Exception ex)
            {
                _lastError = $"Error fetching calendar events: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error fetching calendar events: {ex.Message}");
            }

            return allEvents;
        }

        /// <summary>
        /// Gets upcoming interview events (events containing keywords like "interview", "meeting", etc.).
        /// </summary>
        public async Task<List<CalendarEvent>> GetUpcomingInterviewsAsync(int maxResults = 10)
        {
            var allEvents = await GetUpcomingEventsAsync(maxResults * 2); // Get more to filter
            var interviewKeywords = new[] { "interview", "meeting", "call", "zoom", "teams", "google meet", "hiring", "recruiter" };

            return allEvents
                .Where(e => interviewKeywords.Any(keyword => 
                    e.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    e.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                .Take(maxResults)
                .ToList();
        }

        /// <summary>
        /// Gets the next interview event.
        /// </summary>
        public async Task<CalendarEvent?> GetNextInterviewAsync()
        {
            var interviews = await GetUpcomingInterviewsAsync(1);
            return interviews.FirstOrDefault();
        }

        /// <summary>
        /// Checks if user is authenticated.
        /// </summary>
        public bool IsAuthenticated => _credential != null && (_credential.Token == null || !_credential.Token.IsStale);

        /// <summary>
        /// Revokes the current credential.
        /// </summary>
        public async Task RevokeAsync()
        {
            if (_credential != null)
            {
                try
                {
                    await _credential.RevokeTokenAsync(CancellationToken.None);
                }
                catch { }
                _credential = null;
                _calendarService = null;
            }
        }
    }

    /// <summary>
    /// Represents a calendar event.
    /// </summary>
    public class CalendarEvent
    {
        public string Id { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Location { get; set; } = string.Empty;
        public string HangoutLink { get; set; } = string.Empty;
        public bool IsAllDay { get; set; }
        public string CalendarName { get; set; } = string.Empty;

        public string TimeRange
        {
            get
            {
                if (IsAllDay)
                    return "All Day";
                
                return $"{StartTime:MMM dd, yyyy HH:mm} - {EndTime:HH:mm}";
            }
        }

        public bool IsUpcoming => StartTime > DateTime.Now;
        public TimeSpan TimeUntilStart => StartTime - DateTime.Now;
    }
}



