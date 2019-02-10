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
        public string FileFilter => "susファイル(*.sus)|*.sus";

        public string DisplayName => "Seaurchin Score File";

        public ScoreBook Import(TextReader reader)
        {
            var sus = new SusParser().Parse(reader);
            return new ScoreBook()
            {
                Title = sus.Title,
                ArtistName = sus.ArtistName,
                NotesDesignerName = sus.DesignerName,
                Score = ConvertScore(sus)
            };
        }

        private Score ConvertScore(SusScoreData raw)
        {
            var res = new Score() { TicksPerBeat = raw.TicksPerBeat };
            res.Events.BPMChangeEvents = raw.BpmDefinitions.Select(p => new BPMChangeEvent() { Tick = p.Key, BPM = p.Value }).ToList();
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
                switch (item.Item1)
                {
                    case '1':
                        res.Notes.Taps.Add(SetNotePosition(new Tap(), item.Item2));
                        break;

                    case '2':
                    case '5':
                    case '6':
                        res.Notes.ExTaps.Add(SetNotePosition(new ExTap(), item.Item2));
                        break;

                    case '3':
                        res.Notes.Flicks.Add(SetNotePosition(new Flick(), item.Item2));
                        break;

                    case '4':
                        res.Notes.Damages.Add(SetNotePosition(new Damage(), item.Item2));
                        break;
                }
            }

            foreach (var hold in raw.LongNotes['2'])
            {
                res.Notes.Holds.Add(new Hold()
                {
                    StartTick = hold[0].Item2.Tick,
                    Duration = hold[1].Item2.Tick - hold[0].Item2.Tick,
                    LaneIndex = hold[0].Item2.LaneIndex,
                    Width = hold[0].Item2.Width
                });
            }

            foreach (var steps in raw.LongNotes['3'])
            {
                var slide = new Slide()
                {
                    StartTick = steps[0].Item2.Tick,
                    StartLaneIndex = steps[0].Item2.LaneIndex,
                    StartWidth = steps[0].Item2.Width
                };
                foreach (var step in steps.Skip(1))
                {
                    var stepTap = new Slide.StepTap(slide)
                    {
                        IsVisible = step.Item1 == '3' || step.Item1 == '2',
                        TickOffset = step.Item2.Tick - slide.StartTick
                    };
                    stepTap.SetPosition(step.Item2.LaneIndex - slide.StartLaneIndex, step.Item2.Width - slide.StartWidth);
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
                if (!airablesDic.ContainsKey(item.Item2)) continue;
                foreach (var airable in airablesDic[item.Item2])
                {
                    if (usedAirs.Contains(airable)) continue;
                    var air = new Air(airable) { VerticalDirection = Regex.IsMatch(item.Item1.ToString(), "1|3|4") ? VerticalAirDirection.Up : VerticalAirDirection.Down };
                    switch (item.Item1)
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
                    res.Notes.Airs.Add(air);
                    usedAirs.Add(airable);
                    break;
                }
            }

            var usedAirActions = new HashSet<IAirable>();
            foreach (var item in raw.LongNotes['4'])
            {
                if (!airablesDic.ContainsKey(item[0].Item2)) continue;
                foreach (var airable in airablesDic[item[0].Item2])
                {
                    if (usedAirActions.Contains(airable)) continue;
                    var airAction = new AirAction(airable);
                    airAction.ActionNotes.AddRange(item.Skip(1).Select(p => new AirAction.ActionNote(airAction) { Offset = p.Item2.Tick - item[0].Item2.Tick }));
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
            note.LaneIndex = pos.LaneIndex;
            note.Width = pos.Width;
            return note;
        }
    }
}
