using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Ched.Core;
using Ched.Core.Notes;
using Ched.Core.Events;
using Sus2Image.Converter;

namespace Ched.Plugins
{
    public class SusImportPlugin : IScoreBookImportPlugin
    {
        public string FileFilter => "Sliding Universal Score(*.sus)|*.sus";

        public string DisplayName => "Sliding Universal Score (*.sus)";

        public ScoreBook Import(IScoreBookImportPluginArgs args)
        {
            using (var reader = new StreamReader(args.Stream))
            {
                var sus = new SusParser() { DiagnosticCollector = new ChedDiagnosticCollector(args) }.Parse(reader);
                return new ScoreBook()
                {
                    Title = sus.Title,
                    ArtistName = sus.ArtistName,
                    NotesDesignerName = sus.DesignerName,
                    Score = ConvertScore(args, sus)
                };
            }
        }

        private Score ConvertScore(IScoreBookImportPluginArgs args, SusScoreData raw)
        {
            var res = new Score() { TicksPerBeat = raw.TicksPerBeat };
            res.Events.BpmChangeEvents = raw.BpmDefinitions.Select(p => new BpmChangeEvent() { Tick = p.Key, Bpm = p.Value }).ToList();
            res.Events.TimeSignatureChangeEvents = raw.TimeSignatures.Select(p =>
            {
                int factor = 1;
                for (int i = 2; i < 10; i++)
                {
                    if (factor * p.Value % 1 == 0)
                        return new TimeSignatureChangeEvent() { Tick = p.Key, Numerator = (int)(factor * p.Value), DenominatorExponent = i };
                    factor *= 2;
                }
                throw new ArgumentException("Invalid time signature");
            }).ToList();

            foreach (var item in raw.ShortNotes['1'])
            {
                switch (item.Type)
                {
                    case '1':
                        res.Notes.Taps.Add(SetNotePosition(new Tap(), item.Position));
                        break;

                    case '2':
                        res.Notes.ExTaps.Add(SetNotePosition(new ExTap(), item.Position));
                        break;

                    case '5':
                    case '6':
                        args.ReportDiagnostic(new Diagnostic(DiagnosticSeverity.Warning, $"やべーExTAPは通常ExTAPとしてインポートされます。(行: {item.LineIndex + 1})"));
                        res.Notes.ExTaps.Add(SetNotePosition(new ExTap(), item.Position));
                        break;

                    case '3':
                        res.Notes.Flicks.Add(SetNotePosition(new Flick(), item.Position));
                        break;

                    case '4':
                        res.Notes.Damages.Add(SetNotePosition(new Damage(), item.Position));
                        break;
                }
            }

            foreach (var hold in raw.LongNotes['2'])
            {
                if (hold.Count != 2)
                {
                    args.ReportDiagnostic(new Diagnostic(DiagnosticSeverity.Warning, $"始点と終点以外を含むホールド定義です。このホールドは無視されます。(行: {string.Join(", ", hold.Select(p => p.LineIndex + 1))})"));
                    continue;
                }
                if (hold[0].Position.LaneIndex != hold[1].Position.LaneIndex || hold[0].Position.Width != hold[1].Position.Width)
                {
                    args.ReportDiagnostic(new Diagnostic(DiagnosticSeverity.Warning, $"始点と終点のレーン位置が対応していないホールド定義です。このホールドは無視されます。(行: {string.Join(", ", hold.Select(p => p.LineIndex + 1))})"));
                    continue;
                }
                res.Notes.Holds.Add(new Hold()
                {
                    StartTick = hold[0].Position.Tick,
                    Duration = hold[1].Position.Tick - hold[0].Position.Tick,
                    LaneIndex = hold[0].Position.LaneIndex,
                    Width = hold[0].Position.Width
                });
            }

            foreach (var steps in raw.LongNotes['3'])
            {
                var slide = new Slide()
                {
                    StartTick = steps[0].Position.Tick,
                    StartLaneIndex = steps[0].Position.LaneIndex,
                    StartWidth = steps[0].Position.Width
                };
                foreach (var step in steps.Skip(1))
                {
                    if (step.Type == '4')
                    {
                        args.ReportDiagnostic(new Diagnostic(DiagnosticSeverity.Warning, $"スライド変曲点は中継点としてインポートされます。(行: {step.LineIndex + 1})"));
                    }
                    var stepTap = new Slide.StepTap(slide)
                    {
                        IsVisible = step.Type == '3' || step.Type == '2',
                        TickOffset = step.Position.Tick - slide.StartTick
                    };
                    stepTap.SetPosition(step.Position.LaneIndex - slide.StartLaneIndex, step.Position.Width - slide.StartWidth);
                    slide.StepNotes.Add(stepTap);
                }
                res.Notes.Slides.Add(slide);
            }

            var airables = res.Notes.GetShortNotes().Cast<IAirable>()
                .Concat(res.Notes.Holds.Select(p => p.EndNote))
                .Concat(res.Notes.Slides.Select(p => p.StepNotes.OrderByDescending(q => q.Tick).First()));
            var airablesDic = airables.Select(p => new { Note = p, Position = new NotePosition() { Tick = p.Tick, LaneIndex = p.LaneIndex, Width = p.Width } })
                .GroupBy(p => p.Position)
                .ToDictionary(p => p.Key, p => p.Select(q => q.Note).ToList());

            var usedAirs = new HashSet<IAirable>();
            foreach (var item in raw.ShortNotes['5'])
            {
                if (!airablesDic.ContainsKey(item.Position)) continue;
                foreach (var airable in airablesDic[item.Position])
                {
                    if (usedAirs.Contains(airable)) continue;
                    var air = new Air(airable);
                    switch (item.Type)
                    {
                        case '1':
                        case '2':
                            air.HorizontalDirection = HorizontalAirDirection.Center;
                            break;

                        case '3':
                        case '5':
                            air.HorizontalDirection = HorizontalAirDirection.Left;
                            break;

                        case '4':
                        case '6':
                            air.HorizontalDirection = HorizontalAirDirection.Right;
                            break;
                    }
                    switch (item.Type)
                    {
                        case '1':
                        case '3':
                        case '4':
                            air.VerticalDirection = VerticalAirDirection.Up;
                            break;

                        case '2':
                        case '5':
                        case '6':
                            air.VerticalDirection = VerticalAirDirection.Down;
                            break;
                    }
                    res.Notes.Airs.Add(air);
                    usedAirs.Add(airable);
                    break;
                }
            }

            var usedAirActions = new HashSet<IAirable>();
            foreach (var item in raw.LongNotes['4'])
            {
                if (!airablesDic.ContainsKey(item[0].Position)) continue;
                foreach (var airable in airablesDic[item[0].Position])
                {
                    if (usedAirActions.Contains(airable)) continue;
                    var airAction = new AirAction(airable);
                    foreach (var note in item.Skip(1))
                    {
                        if (note.Position.LaneIndex != item[0].Position.LaneIndex || note.Position.Width != item[0].Position.Width)
                        {
                            args.ReportDiagnostic(new Diagnostic(DiagnosticSeverity.Warning, $"レーン位置が対応していないAir Action定義です。このAir Actionは無視されます。(行: {note.LineIndex + 1})"));
                            continue;
                        }
                        var actionNote = new AirAction.ActionNote(airAction) { Offset = note.Position.Tick - item[0].Position.Tick };
                        airAction.ActionNotes.Add(actionNote);
                    }
                    res.Notes.AirActions.Add(airAction);
                    usedAirActions.Add(airable);
                    break;
                }
            }

            return res;
        }

        private T SetNotePosition<T>(T note, NotePosition pos) where T : TappableBase
        {
            note.Tick = pos.Tick;
            note.SetPosition(pos.LaneIndex, pos.Width);
            return note;
        }
    }

    public class ChedDiagnosticCollector : IDiagnosticCollector
    {
        private IScoreBookImportPluginArgs ScorePluginArgs;

        public ChedDiagnosticCollector(IScoreBookImportPluginArgs args)
        {
            ScorePluginArgs = args;
        }

        public void ReportError(string message)
        {
            ScorePluginArgs.ReportDiagnostic(new Diagnostic(DiagnosticSeverity.Error, message));
        }

        public void ReportInformation(string message)
        {
            ScorePluginArgs.ReportDiagnostic(new Diagnostic(DiagnosticSeverity.Information, message));
        }

        public void ReportWarning(string message)
        {
            ScorePluginArgs.ReportDiagnostic(new Diagnostic(DiagnosticSeverity.Warning, message));
        }
    }
}
