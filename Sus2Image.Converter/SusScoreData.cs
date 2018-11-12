using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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

    public class SusParser
    {
        private Regex SusCommandPattern { get; } = new Regex(@"#(?<name>[A-Z]+)\s+\""?(?<value>[^\""]*)", RegexOptions.IgnoreCase);
        private Regex SusNotePattern { get; } = new Regex(@"#(?<barIndex>\d{3})(?<type>[1-5])(?<laneIndex>[0-9a-f])(?<longKey>[0-9a-z])?:(?<data>([0-9a-g]{2}|\s)+)", RegexOptions.IgnoreCase);
        private Regex BpmDefinitionPattern { get; } = new Regex(@"#BPM(?<key>[0-9a-f]{2}):\s*(?<value>[0-9.]+)", RegexOptions.IgnoreCase);
        private Regex BpmCommandPattern { get; } = new Regex(@"#(?<barIndex>\d{3})08:\s*(?<data>[0-9a-f\s]+)", RegexOptions.IgnoreCase);
        private Regex TimeSignatureCommandPattern { get; } = new Regex(@"(?<barIndex>\d{3})02:\s*(?<value>[0-9.]+)");

        private int TicksPerBeat = 192;
        private Dictionary<string, decimal> BpmDefinitions = new Dictionary<string, decimal>();
        private SusScoreData Score = new SusScoreData();

        public SusScoreData Parse(StreamReader reader)
        {
            var sigs = new Dictionary<int, double>();

            var shortNotesData = new List<Match>();
            var longNotesData = new List<Match>();
            var bpmData = new List<Match>();
            Func<Match, Action<Match>, bool> matchAction = (m, act) =>
            {
                if (m.Success) act(m);
                return m.Success;
            };

            while (reader.Peek() >= 0)
            {
                string line = reader.ReadLine();

                if (matchAction(SusCommandPattern.Match(line), m => ProcessCommand(m.Groups["name"].Value, m.Groups["value"].Value))) continue;

                if (matchAction(BpmDefinitionPattern.Match(line), m => BpmDefinitions.Add(m.Groups["key"].Value, decimal.Parse(m.Groups["value"].Value)))) continue;

                if (matchAction(BpmCommandPattern.Match(line), m => bpmData.Add(m))) continue;

                if (matchAction(SusNotePattern.Match(line), m => (m.Groups["longKey"].Success ? longNotesData : shortNotesData).Add(m))) continue;

                if (matchAction(TimeSignatureCommandPattern.Match(line), m => sigs.Add(int.Parse(m.Groups["barIndex"].Value), double.Parse(m.Groups["value"].Value)))) continue;
            }

            var barIndexCalculator = new BarIndexCalculator(TicksPerBeat, sigs);

            var bpmDic = bpmData.SelectMany(p => SplitData(barIndexCalculator, int.Parse(p.Groups["barIndex"].Value), p.Groups["data"].Value))
                .Where(p => p.Item2 != "00")
                .ToDictionary(p => p.Item1, p => BpmDefinitions[p.Item2]);

            // データ種別と位置
            var shortNotes = shortNotesData.GroupBy(p => p.Groups["type"].Value[0]).ToDictionary(p => p.Key, p => p.SelectMany(q =>
            {
                return SplitData(barIndexCalculator, int.Parse(q.Groups["barIndex"].Value), q.Groups["data"].Value)
                    .Where(r => r.Item2 != "00")
                    .Select(r => Tuple.Create(r.Item2[0], new NotePosition() { Tick = r.Item1, LaneIndex = ConvertHex(q.Groups["laneIndex"].Value[0]), Width = ConvertHex(r.Item2[1]) }));
            }).ToList());

            // ロング種別 -> ロングノーツリスト -> 構成点リスト
            var longNotes = longNotesData.GroupBy(p => p.Groups["type"].Value[0]).ToDictionary(p => p.Key, p =>
            {
                return p.GroupBy(q => q.Groups["longKey"].Value.ToUpper()).Select(q => q.SelectMany(r =>
                {
                    return SplitData(barIndexCalculator, int.Parse(r.Groups["barIndex"].Value), r.Groups["data"].Value)
                        .Where(s => s.Item2 != "00")
                        .Select(s => Tuple.Create(s.Item2[0], new NotePosition() { Tick = s.Item1, LaneIndex = ConvertHex(r.Groups["laneIndex"].Value[0]), Width = ConvertHex(s.Item2[1]) }));
                }))
                .SelectMany(q => p.Key == '2' ? q.GroupBy(r => r.Item2.LaneIndex).SelectMany(r => FlatSplitLongNotes(r, '1', '2')) : FlatSplitLongNotes(q, '1', '2'))
                .ToList();
            });


            return new SusScoreData()
            {
                TicksPerBeat = this.TicksPerBeat,
                BpmDefinitions = bpmDic,
                TimeSignatures = sigs.ToDictionary(p => barIndexCalculator.GetTickFromBarIndex(p.Key), p => p.Value),
                ShortNotes = shortNotes,
                LongNotes = longNotes
            };
        }

        protected void ProcessCommand(string name, string value)
        {
            switch (name.ToUpper())
            {
                case "REQUEST":
                    var tpb = Regex.Match(value, @"(?<=ticks_per_beat )\d+");
                    if (tpb.Success) TicksPerBeat = int.Parse(tpb.Value);
                    break;
            }
        }

        // 同一識別子を持つロングノーツのリストをロングノーツ単体に分解します
        protected IEnumerable<List<Tuple<char, NotePosition>>> FlatSplitLongNotes(IEnumerable<Tuple<char, NotePosition>> data, char beginChar, char endChar)
        {
            var enumerator = data.OrderBy(p => p.Item2.Tick).GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Item1 != beginChar) throw new InvalidOperationException();
                var longNote = new List<Tuple<char, NotePosition>>() { enumerator.Current };
                while (enumerator.MoveNext())
                {
                    longNote.Add(enumerator.Current);
                    if (enumerator.Current.Item1 == endChar) break;
                }

                yield return longNote;
            }
        }

        protected IEnumerable<Tuple<int, string>> SplitData(BarIndexCalculator c, int barIndex, string data)
        {
            data = Regex.Replace(data, @"\s+", "");
            int headTick = c.GetTickFromBarIndex(barIndex);
            int barTick = (int)(c.GetBarBeatsFromBarIndex(barIndex) * TicksPerBeat);
            var list = Enumerable.Range(0, data.Length / 2).Select(p => data.Substring(p * 2, 2)).ToList();
            return list.Select((p, i) => Tuple.Create(headTick + (int)(barTick * ((double)i / list.Count)), p));
        }

        protected int ConvertHex(char c)
        {
            return c == 'g' ? 16 : Convert.ToInt32(c.ToString(), 16);
        }
    }

    public class BarIndexCalculator
    {
        private int TicksPerBeat { get; } = 192;
        private List<Tuple<int, int, double>> Definitions { get; } = new List<Tuple<int, int, double>>(); // barIndex, headTick, barBeats

        public BarIndexCalculator(int ticksPerBeat, Dictionary<int, double> sigs) // barIndex, barBeats
        {
            TicksPerBeat = ticksPerBeat;
            var ordered = sigs.OrderBy(p => p.Key).ToList();
            int pos = 0;

            for (int i = 0; i < ordered.Count; i++)
            {
                Definitions.Add(Tuple.Create(ordered[i].Key, pos, ordered[i].Value));
                if (i < ordered.Count - 1)
                    pos += (int)((ordered[i + 1].Key - ordered[i].Key) * ticksPerBeat * ordered[i].Value);
            }
        }

        public int GetTickFromBarIndex(int barIndex)
        {
            var def = GetDefinitionFromBarIndex(barIndex);
            return def.Item2 + (int)((barIndex - def.Item1) * TicksPerBeat * def.Item3);

        }

        public double GetBarBeatsFromBarIndex(int barIndex)
        {
            return GetDefinitionFromBarIndex(barIndex).Item3;
        }

        protected Tuple<int, int, double> GetDefinitionFromBarIndex(int barIndex)
        {
            int i = 0;
            while (i < Definitions.Count && barIndex >= Definitions[i].Item1) i++;
            return Definitions[i - 1];
        }
    }

    public struct NotePosition
    {
        public int Tick { get; set; }
        public int LaneIndex { get; set; }
        public int Width { get; set; }
    }
}
