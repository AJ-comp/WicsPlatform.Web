using System;
using System.Collections.Generic;
using System.Linq;

namespace WicsPlatform.Client.Services
{
    public class BroadcastLoggingService
    {
        private readonly List<BroadcastLogEntry> _logBuffer = new();
        private readonly int _maxBufferSize = 100;

        public event Action<BroadcastLogEntry> OnLogAdded;
        public event Action OnLogsCleared;

        public IReadOnlyList<BroadcastLogEntry> GetBufferedLogs() => _logBuffer.AsReadOnly();

        public void AddLog(string level, string message)
        {
            var logEntry = new BroadcastLogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };

            _logBuffer.Add(logEntry);

            // 버퍼 크기 제한
            if (_logBuffer.Count > _maxBufferSize)
            {
                _logBuffer.RemoveAt(0);
            }

            OnLogAdded?.Invoke(logEntry);
        }

        public void ClearLogs()
        {
            _logBuffer.Clear();
            OnLogsCleared?.Invoke();
        }
    }

    public class BroadcastLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }
}