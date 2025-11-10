using System;
using System.Text;

namespace StealthInterviewAssistant.Services
{
    public class CaptionService
    {
        private readonly StringBuilder _fullTranscript = new StringBuilder();
        private string _lastCapturePoint = string.Empty;
        private bool _isRunning = false;

        public void Start()
        {
            if (_isRunning) return;
            
            _isRunning = true;
            _lastCapturePoint = _fullTranscript.ToString();
            // TODO: Implement actual caption capture logic
        }

        public void Stop()
        {
            _isRunning = false;
        }

        public void AppendText(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                _fullTranscript.Append(text);
            }
        }

        public string GetDelta()
        {
            string current = _fullTranscript.ToString();
            if (current.Length > _lastCapturePoint.Length)
            {
                string delta = current.Substring(_lastCapturePoint.Length);
                _lastCapturePoint = current;
                return delta;
            }
            return string.Empty;
        }

        public string GetFullTranscript()
        {
            return _fullTranscript.ToString();
        }

        public void Clear()
        {
            _fullTranscript.Clear();
            _lastCapturePoint = string.Empty;
        }

        public bool IsRunning => _isRunning;
    }
}

