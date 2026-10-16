using System;

namespace mRemoteNG.Tools
{
    public static class ExceptionExtensions
    {
        /// <summary>The exception's message followed by the messages of all its inner exceptions, one per line.</summary>
        public static string GetExceptionMessageRecursive(this Exception ex)
        {
            return GetExceptionMessageRecursive(ex, Environment.NewLine);
        }

        private static string GetExceptionMessageRecursive(Exception ex, string separator)
        {
            string message = ex.Message;
            if (ex.InnerException == null) return message;
            string innerMessage = GetExceptionMessageRecursive(ex.InnerException, separator);
            message = String.Join(separator, message, innerMessage);
            return message;
        }
    }
}
