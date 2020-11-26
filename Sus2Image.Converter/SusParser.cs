using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sus2Image.Converter
{
    public class SusParser
    {
        private Regex SusCommandPattern { get; } = new Regex(@"#(?<name>[A-Z]+)\s+\""?(?<value>[^\""]*)", RegexOptions.IgnoreCase);
        private Regex SusNotePattern { get; } = new Regex(@"#(?<barIndex>\d{3})(?<type>[1-5])(?<laneIndex>[0-9a-f])(?<longKey>[0-9a-z])?:(?<data>([0-9a-gh-z]{2}|\s)+)", RegexOptions.IgnoreCase);
        private Regex BpmDefinitionPattern { get; } = new Regex(@"#BPM(?<key>[0-9a-z]{2}):\s*(?<value>[0-9.]+)", RegexOptions.IgnoreCase);
        private Regex BpmCommandPattern { get; } = new Regex(@"#(?<barIndex>\d{3})08:\s*(?<data>[0-9a-z\s]+)", RegexOptions.IgnoreCase);
        private Regex TimeSignatureCommandPattern { get; } = new Regex(@"(?<barIndex>\d{3})02:\s*(?<value>[0-9.]+)");

        public IDiagnosticCollector DiagnosticCollector { get; set; } = new NullDiagnosticCollector();

        private int TicksPerBeat = 192;
        private Dictionary<string, double> BpmDefinitions = new Dictionary<string, double>();
        private string Title;
        private string ArtistName;
        private string DesignerName;

        public SusScoreData Parse(TextReader reader)
        {
            var sigs = new Dictionary<int, double>();

            var shortNotesData = new List<(int LineIndex, Match Match)>();
            var longNotesData = new List<(int LineIndex, Match Match)>();
            var bpmData = new List<(int LineIndex, Match Match)>();
            bool matchAction(Match m, Action<Match> act)
            {
                if (m.Success) act(m);
                return m.Success;
            }

            int lineIndex = -1;
            while (reader.Peek() >= 0)
            {
                string line = reader.ReadLine();
                lineIndex++;

                if (matchAction(SusCommandPattern.Match(line), m => ProcessCommand(lineIndex, m.Groups["name"].Value, m.Groups["value"].Value))) continue;

                if (matchAction(BpmDefinitionPattern.Match(line), m => StoreBpmDefinition(lineIndex, m.Groups["key"].Value, double.Parse(m.Groups["value"].Value)))) continue;

                if (matchAction(BpmCommandPattern.Match(line), m => bpmData.Add((lineIndex, m)))) continue;

                if (matchAction(SusNotePattern.Match(line), m => (m.Groups["longKey"].Success ? longNotesData : shortNotesData).Add((lineIndex, m)))) continue;

                if (matchAction(TimeSignatureCommandPattern.Match(line), m => sigs.Add(int.Parse(m.Groups["barIndex"].Value), double.Parse(m.Groups["value"].Value)))) continue;
            }

            if (!sigs.ContainsKey(0))
            {
                sigs.Add(0, 4.0);
                DiagnosticCollector.ReportInformation("初期拍子定義が存在しないため、4/4拍子に設定します。");
            }

            var barIndexCalculator = new BarIndexCalculator(TicksPerBeat, sigs);

            var bpmDic = bpmData.SelectMany(p => SplitData(barIndexCalculator, p.LineIndex, int.Parse(p.Match.Groups["barIndex"].Value), p.Match.Groups["data"].Value).Select(q => new { LineIndex = p.LineIndex, Definition = q }))
                .Where(p =>
                {
                    if (p.Definition.Data == "00") return false;
                    if (!BpmDefinitions.ContainsKey(p.Definition.Data.ToLower()))
                    {
                        DiagnosticCollector.ReportWarning($"BPM定義番号{p.Definition.Data}は宣言されていません。このBPM変更は無視されます。(行: {p.LineIndex + 1})");
                        return false;
                    }
                    return true;
                })
                .ToDictionary(p => p.Definition.Tick, p => BpmDefinitions[p.Definition.Data.ToLower()]);

            if (!bpmDic.ContainsKey(0))
            {
                bpmDic.Add(0, 120);
                DiagnosticCollector.ReportInformation("初期BPM定義が存在しないため、120に設定します。");
            }

            // データ種別と位置
            var shortNotes = shortNotesData.GroupBy(p => p.Match.Groups["type"].Value[0]).ToDictionary(p => p.Key, p => p.SelectMany(q =>
            {
                return SplitData(barIndexCalculator, q.LineIndex, int.Parse(q.Match.Groups["barIndex"].Value), q.Match.Groups["data"].Value)
                    .Where(r => Regex.IsMatch(r.Data, "[1-9][1-9a-g]", RegexOptions.IgnoreCase))
                    .Select(r => new NoteDefinition() { LineIndex = q.LineIndex, Type = r.Data[0], Position = new NotePosition() { Tick = r.Tick, LaneIndex = ConvertHex(q.Match.Groups["laneIndex"].Value[0]), Width = ConvertHex(r.Data[1]) } });
            }).ToList());

            // ロング種別 -> ロングノーツリスト -> 構成点リスト
            var longNotes = longNotesData.GroupBy(p => p.Match.Groups["type"].Value[0]).ToDictionary(p => p.Key, p =>
            {
                return p.GroupBy(q => q.Match.Groups["longKey"].Value.ToUpper()).Select(q => q.SelectMany(r =>
                {
                    return SplitData(barIndexCalculator, lineIndex, int.Parse(r.Match.Groups["barIndex"].Value), r.Match.Groups["data"].Value)
                        .Where(s => Regex.IsMatch(s.Data, "[1-5][1-9a-g]", RegexOptions.IgnoreCase))
                        .Select(s => new NoteDefinition() { LineIndex = r.LineIndex, Type = s.Data[0], Position = new NotePosition() { Tick = s.Tick, LaneIndex = ConvertHex(r.Match.Groups["laneIndex"].Value[0]), Width = ConvertHex(s.Data[1]) } });
                }))
                .SelectMany(q => p.Key == '2' ? q.GroupBy(r => r.Position.LaneIndex).SelectMany(r => FlatSplitLongNotes(r, '1', '2')) : FlatSplitLongNotes(q, '1', '2'))
                .ToList();
            });

            foreach (char c in new[] { '1', '5' })
                FillKey(shortNotes, c, new List<NoteDefinition>());

            foreach (char c in new[] { '2', '3', '4', })
                FillKey(longNotes, c, new List<List<NoteDefinition>>());


            return new SusScoreData()
            {
                TicksPerBeat = this.TicksPerBeat,
                BpmDefinitions = bpmDic,
                TimeSignatures = sigs.ToDictionary(p => barIndexCalculator.GetTickFromBarIndex(p.Key), p => p.Value),
                Title = this.Title,
                ArtistName = this.ArtistName,
                DesignerName = this.DesignerName,
                ShortNotes = shortNotes,
                LongNotes = longNotes
            };
        }

        protected void ProcessCommand(int lineIndex, string name, string value)
        {
            switch (name.ToUpper())
            {
                case "TITLE":
                    Title = TrimLiteral(value);
                    break;

                case "ARTIST":
                    ArtistName = TrimLiteral(value);
                    break;

                case "DESIGNER":
                    DesignerName = TrimLiteral(value);
                    break;

                case "REQUEST":
                    var tpb = Regex.Match(value, @"(?<=ticks_per_beat )\d+");
                    if (tpb.Success)
                    {
                        TicksPerBeat = int.Parse(tpb.Value);
                        DiagnosticCollector.ReportInformation($"ticks per beatを{TicksPerBeat}に変更しました。");
                    }
                    else
                    {
                        DiagnosticCollector.ReportWarning($"処理されないREQUESTコマンドです。(行: {lineIndex + 1})");
                    }
                    break;

                default:
                    DiagnosticCollector.ReportWarning($"{name}コマンドは処理されません。(行: {lineIndex + 1})");
                    break;
            }
        }

        protected void StoreBpmDefinition(int lineIndex, string key, double value)
        {
            key = key.ToLower();
            if (key == "00")
            {
                DiagnosticCollector.ReportWarning($"BPM定義番号00は無効です。この定義は無視されます。(行: {lineIndex + 1}, BPM値: {value})");
                return;
            }
            if (BpmDefinitions.ContainsKey(key))
            {
                DiagnosticCollector.ReportWarning($"重複したBPM定義です。先行する定義を上書きします。(行: {lineIndex + 1}, 定義番号: {key})");
                BpmDefinitions[key] = value;
                return;
            }
            BpmDefinitions.Add(key, value);
        }

        // 同一識別子を持つロングノーツのリストをロングノーツ単体に分解します
        protected IEnumerable<List<NoteDefinition>> FlatSplitLongNotes(IEnumerable<NoteDefinition> data, char beginChar, char endChar)
        {
            var enumerator = data.OrderBy(p => p.Position.Tick).ThenByDescending(p => p.Type).GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Type != beginChar)
                {
                    DiagnosticCollector.ReportWarning($"終点に対応する始点がありません。このロングノート定義は無視されます。(行: {enumerator.Current.LineIndex + 1}, 種類: {enumerator.Current.Type})");
                    continue;
                }
                var longNote = new List<NoteDefinition>() { enumerator.Current };
                while (enumerator.MoveNext())
                {
                    if (enumerator.Current.Type == beginChar)
                    {
                        DiagnosticCollector.ReportWarning($"始点に対応する終点がありません。このロングノート開始定義は無視されます。(行: {enumerator.Current.LineIndex + 1}, 種類: {enumerator.Current.Type})");
                        continue;
                    }
                    if (longNote[longNote.Count - 1].Position.Tick == enumerator.Current.Position.Tick)
                    {
                        DiagnosticCollector.ReportWarning($"同一の時刻に定義が重複しています。このロングノート定義は無視されます。(行: {enumerator.Current.LineIndex + 1}, 種類: {enumerator.Current.Type})");
                        continue;
                    }
                    longNote.Add(enumerator.Current);
                    if (enumerator.Current.Type == endChar) break;
                }
                if (enumerator.Current.Type != endChar)
                {
                    DiagnosticCollector.ReportWarning($"始点に対応する終点がありません。このロングノート定義は無視されます。(行: {enumerator.Current.LineIndex + 1}, 種類: {enumerator.Current.Type})");
                }

                yield return longNote;
            }
        }

        protected IEnumerable<(int Tick, string Data)> SplitData(BarIndexCalculator c, int lineIndex, int barIndex, string data)
        {
            data = Regex.Replace(data, @"\s+", "");
            if (data.Length % 2 != 0)
            {
                DiagnosticCollector.ReportWarning($"データ定義の文字列長が2の倍数ではありません。終端部分は無視されます。(行: {lineIndex + 1})");
            }
            int headTick = c.GetTickFromBarIndex(barIndex);
            int barTick = (int)(c.GetBarBeatsFromBarIndex(barIndex) * TicksPerBeat);
            var list = Enumerable.Range(0, data.Length / 2).Select(p => data.Substring(p * 2, 2)).ToList();
            return list.Select((p, i) => (headTick + (int)(barTick * ((double)i / list.Count)), p));
        }

        protected int ConvertHex(char c)
        {
            string s = c.ToString().ToLower();
            return s == "g" ? 16 : Convert.ToInt32(s, 16);
        }

        protected string TrimLiteral(string str)
        {
            return Regex.Match(str, @"(?<=\""?)[^\""]*").Value;
        }

        protected void FillKey<TKey, TValue>(IDictionary<TKey, TValue> dic, TKey key, TValue defaultValue)
        {
            if (!dic.ContainsKey(key)) dic.Add(key, defaultValue);
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
}
