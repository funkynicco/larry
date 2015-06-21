using System;

namespace Larry.Network
{
    /// <summary>
    /// Thrown when a value in a packet is wrong and the client must be disconnected.
    /// </summary>
    public class DataValidationException : Exception
    {
        /// <summary>
        /// Thrown when a value in a packet is wrong and the client must be disconnected.
        /// </summary>
        /// <param name="message">Disconnect message</param>
        public DataValidationException(string message) :
            base(message)
        {
        }

        /// <summary>
        /// Thrown when a value in a packet is wrong and the client must be disconnected.
        /// </summary>
        /// <param name="format">Disconnect message</param>
        public DataValidationException(string format, params object[] args) :
            base(string.Format(format, args))
        {
        }
    }
}
