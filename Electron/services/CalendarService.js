const { google } = require('googleapis');
const path = require('path');
const fs = require('fs');
const { app } = require('electron');

class CalendarService {
  constructor() {
    this.calendarService = null;
    this.oauth2Client = null;
    this.isAuthenticated = false;
    this.clientId = null;
    this.clientSecret = null;
    this.lastError = null;
    this.credentialPath = path.join(app.getPath('userData'), 'google-calendar-credentials.json');
  }

  initialize() {
    // Load credentials from config file (similar to Windows version)
    this.loadCredentials();
  }

  loadCredentials() {
    try {
      const configPath = path.join(app.getPath('userData'), 'config.json');
      if (fs.existsSync(configPath)) {
        const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
        if (config.googleCalendar) {
          this.clientId = config.googleCalendar.clientId;
          this.clientSecret = config.googleCalendar.clientSecret;
        }
      }
    } catch (error) {
      console.error('Error loading calendar credentials:', error);
    }
  }

  async connect() {
    this.lastError = null;

    if (!this.clientId || !this.clientSecret) {
      this.lastError = 'Client ID or Client Secret is missing. Please check config.json';
      return { success: false, error: this.lastError };
    }

    try {
      this.oauth2Client = new google.auth.OAuth2(
        this.clientId,
        this.clientSecret,
        'urn:ietf:wg:oauth:2.0:oob'
      );

      // Check if we have stored credentials
      let token = null;
      if (fs.existsSync(this.credentialPath)) {
        try {
          token = JSON.parse(fs.readFileSync(this.credentialPath, 'utf8'));
          this.oauth2Client.setCredentials(token);
        } catch (error) {
          console.error('Error loading stored credentials:', error);
        }
      }

      // If no token or token is expired, get new one
      if (!token || this.oauth2Client.isTokenExpiring()) {
        const authUrl = this.oauth2Client.generateAuthUrl({
          access_type: 'offline',
          scope: ['https://www.googleapis.com/auth/calendar.readonly']
        });

        // Open browser for authentication
        const { shell } = require('electron');
        await shell.openExternal(authUrl);

        // In a real implementation, you'd use a callback server or show a dialog
        // For now, we'll return an error asking user to paste the code
        this.lastError = 'Please complete authentication in the browser and use the refresh button after authorizing.';
        return { success: false, error: this.lastError, authUrl };
      }

      // Create calendar service
      this.calendarService = google.calendar({
        version: 'v3',
        auth: this.oauth2Client
      });

      this.isAuthenticated = true;
      return { success: true };
    } catch (error) {
      this.lastError = error.message;
      console.error('Calendar connection error:', error);
      return { success: false, error: this.lastError };
    }
  }

  async refresh() {
    if (!this.isAuthenticated || !this.calendarService) {
      return { success: false, error: 'Not connected to Google Calendar' };
    }

    try {
      const events = await this.getUpcomingEvents();
      return { success: true, events };
    } catch (error) {
      this.lastError = error.message;
      return { success: false, error: this.lastError };
    }
  }

  async getUpcomingEvents(maxResults = 10) {
    if (!this.isAuthenticated || !this.calendarService) {
      return [];
    }

    try {
      const response = await this.calendarService.events.list({
        calendarId: 'primary',
        timeMin: new Date().toISOString(),
        maxResults: maxResults * 2,
        singleEvents: true,
        orderBy: 'startTime'
      });

      const events = (response.data.items || []).map(event => {
        const start = event.start.dateTime || event.start.date;
        const end = event.end.dateTime || event.end.date;
        const isAllDay = !event.start.dateTime;

        let timeRange = '';
        if (isAllDay) {
          timeRange = 'All Day';
        } else {
          const startDate = new Date(start);
          const endDate = new Date(end);
          timeRange = `${startDate.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' })} - ${endDate.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}`;
        }

        return {
          id: event.id,
          summary: event.summary || 'No Title',
          description: event.description || '',
          startTime: new Date(start),
          endTime: new Date(end),
          timeRange,
          isAllDay,
          calendarName: 'Primary'
        };
      });

      // Filter for interview-related events
      const interviewKeywords = ['interview', 'meeting', 'call', 'zoom', 'teams', 'google meet', 'hiring', 'recruiter'];
      const interviewEvents = events.filter(event =>
        interviewKeywords.some(keyword =>
          event.summary.toLowerCase().includes(keyword) ||
          event.description.toLowerCase().includes(keyword)
        )
      ).slice(0, maxResults);

      return interviewEvents;
    } catch (error) {
      console.error('Error fetching calendar events:', error);
      this.lastError = error.message;
      return [];
    }
  }

  cleanup() {
    this.calendarService = null;
    this.oauth2Client = null;
    this.isAuthenticated = false;
  }
}

// Export singleton instance
const calendarService = new CalendarService();
module.exports = calendarService;

