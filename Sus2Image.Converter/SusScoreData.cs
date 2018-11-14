using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Ched.Core.Notes;

namespace Sus2Image.Converter
{
    public class SusScoreData
    {
        public int TicksPerBeat { get; set; } = 192;
        public Dictionary<int, decimal> BpmDefinitions { get; set; } = new Dictionary<int, decimal>() { { 0, 120.0m } };
        public Dictionary<int, double> TimeSignatures { get; set; } = new Dictionary<int, double>() { { 0, 4.0 } };

        public Dictionary<char, List<Tuple<char, NotePosition>>> ShortNotes { get; set; } = new Dictionary<char, List<Tuple<char, NotePosition>>>();
        public Dictionary<char, List<List<Tuple<char, NotePosition>>>> LongNotes { get; set; }

        public int GetLastTick()
        {
            return Math.Max(ShortNotes.Max(p => p.Value.Max(q => q.Item2.Tick)), LongNotes.Max(p => p.Value.Max(r => r.Max(s => s.Item2.Tick))));
        }
    }

    public struct NotePosition
    {
        public int Tick { get; set; }
        public int LaneIndex { get; set; }
        public int Width { get; set; }
    }
}
