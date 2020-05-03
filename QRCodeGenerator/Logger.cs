using System;
using System.IO;

namespace QRCodeGenerator
{
    public static class Logger
    {
        private static readonly StreamWriter _logWriter;

        static Logger()
        {
            _logWriter = new StreamWriter("Log.txt", true);
            WriteLog("Started");
        }

        public static void WriteLog(Exception ex, string message = null)
        {
            WriteLog($"# Error # {message} Message: {ex.Message}. Stack trace:\r\n{ex.StackTrace}\r\n");
        }

        public static void WriteLog(string message)
        {
            _logWriter.WriteLine($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} - {message}");
        }

        public static string ToHexString(byte value) => Convert.ToString(value, 16).ToUpper().PadLeft(2, '0');

        public static void Dispose()
        {
            WriteLog("Closing");
            _logWriter.Close();
        }
    }
}
