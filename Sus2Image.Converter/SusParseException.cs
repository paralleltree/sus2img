using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sus2Image.Converter
{
    [Serializable]
    public class SusParseException : Exception
    {
        public SusParseException()
        {
        }

        public SusParseException(string message) : base(message)
        {
        }

        public SusParseException(string message, Exception inner) : base(message, inner)
        {
        }

        protected SusParseException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context)
        {
        }
    }
}
