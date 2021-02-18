using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sus2Image.Converter
{
    public interface IDiagnosticCollector
    {
        void ReportInformation(string message);
        void ReportWarning(string message);
        void ReportError(string message);
    }

    public class NullDiagnosticCollector : IDiagnosticCollector
    {
        public void ReportError(string message)
        {
        }

        public void ReportInformation(string message)
        {
        }

        public void ReportWarning(string message)
        {
        }
    }
}
