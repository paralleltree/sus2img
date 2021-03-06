﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Sus2Image.Converter;
using ConcurrentPriorityQueue;
using Ched.Core.Notes;
using Ched.Drawing;

namespace Sus2Image.Imaging
{
    public class Sus2Image
    {
        protected SusScoreData Score { get; set; }
        protected int TicksPerBeat => Score.TicksPerBeat;
        public MeasurementProfile MeasurementProfile { get; set; }
        public ColorProfile NoteColorProfile { get; set; }
        public BackgroundColorProfile BackgroundColorProfile { get; set; }

        public Sus2Image(StreamReader reader)
        {
            Score = new SusParser().Parse(reader);
        }

        public Image Convert()
        {
            var sigs = Score.TimeSignatures.OrderBy(p => p.Key).ToList();
            int lanesCount = Ched.Core.Constants.LanesCount;
            int columnTick = MeasurementProfile.CalcColumnTickFromTicksPerBeat(Score.TicksPerBeat);
            int paddingTick = MeasurementProfile.CalcPaddingTickFromTicksPerBeat(Score.TicksPerBeat);
            float laneWidth = MeasurementProfile.UnitLaneWidth + MeasurementProfile.BorderThickness;
            float wholeLaneWidth = laneWidth * lanesCount;
            float columnHeight = columnTick * MeasurementProfile.UnitBeatHeight / Score.TicksPerBeat;
            float paddingHeight = paddingTick * MeasurementProfile.UnitBeatHeight / Score.TicksPerBeat;
            int headTick = 0;

            var bmp = new Bitmap((int)(MeasurementProfile.PaddingWidth * 2 + wholeLaneWidth) * (int)Math.Ceiling((double)Score.GetLastTick() / columnTick), (int)(paddingHeight * 2 + columnHeight));
            var longEndsPos = new HashSet<NotePosition>(Score.LongNotes['2'].Concat(Score.LongNotes['3']).Select(p => p.Last().Position));
            var unUsedShortNoteQueues = Score.ShortNotes.ToDictionary(p => p.Key, p =>
            {
                var q = new ConcurrentPriorityQueue<NoteDefinition, int>();
                foreach (var item in p.Value) q.Enqueue(item, -item.Position.Tick);
                return q;
            });
            var usingShortNoteQueues = Score.ShortNotes.ToDictionary(p => p.Key, p => new ConcurrentPriorityQueue<NoteDefinition, int>());
            var unUsedLongNoteQueues = Score.LongNotes.ToDictionary(p => p.Key, p =>
            {
                var q = new ConcurrentPriorityQueue<List<NoteDefinition>, int>();
                foreach (var item in p.Value) q.Enqueue(item, -item[0].Position.Tick);
                return q;
            });
            var usingLongNoteQueues = Score.LongNotes.ToDictionary(p => p.Key, p => new ConcurrentPriorityQueue<List<NoteDefinition>, int>());
            var emptyLong = Enumerable.Empty<List<NoteDefinition>>();

            bool visibleStep(NoteDefinition p) => p.Type == '1' || p.Type == '2' || p.Type == '3';
            void moveToOrigin(Graphics g) => g.TranslateTransform(MeasurementProfile.PaddingWidth, paddingHeight + columnHeight - 1, MatrixOrder.Append);
            void moveToColumn(Graphics g, int columnsCount) => g.TranslateTransform((MeasurementProfile.PaddingWidth * 2 + wholeLaneWidth) * columnsCount, 0, MatrixOrder.Append);
            void moveToHead(Graphics g, int head) => g.TranslateTransform(0, head * MeasurementProfile.UnitBeatHeight / TicksPerBeat, MatrixOrder.Append);

            using (var g = Graphics.FromImage(bmp))
            {
                int columnsCount = 0;
                SizeF strSize = g.MeasureString("000", MeasurementProfile.Font);
                var dc = new DrawingContext(g, NoteColorProfile);
                g.Clear(BackgroundColorProfile.BackgroundColor);

                while (unUsedShortNoteQueues.Any(p => p.Value.Count > 0) || usingShortNoteQueues.Any(p => p.Value.Count > 0) || usingLongNoteQueues.Any(p => p.Value.Count > 0) || unUsedLongNoteQueues.Any(p => p.Value.Count > 0))
                {
                    int tailTick = headTick + columnTick;

                    // 範囲外に出たノーツの更新
                    foreach (var type in usingShortNoteQueues)
                    {
                        while (type.Value.Count > 0 && type.Value.Peek().Position.Tick < headTick - paddingTick) type.Value.Dequeue();
                    }

                    foreach (var type in usingLongNoteQueues)
                    {
                        while (type.Value.Count > 0 && type.Value.Peek()[type.Value.Peek().Count - 1].Position.Tick < headTick - paddingTick) type.Value.Dequeue();
                    }

                    // 範囲内に入るノーツの更新
                    foreach (var type in unUsedShortNoteQueues)
                    {
                        while (type.Value.Count > 0 && type.Value.Peek().Position.Tick < tailTick + paddingTick)
                        {
                            var item = type.Value.Dequeue();
                            usingShortNoteQueues[type.Key].Enqueue(item, -item.Position.Tick);
                        }
                    }

                    foreach (var type in unUsedLongNoteQueues)
                    {
                        while (type.Value.Count > 0 && type.Value.Peek()[0].Position.Tick < tailTick + paddingTick)
                        {
                            var item = type.Value.Dequeue();
                            usingLongNoteQueues[type.Key].Enqueue(item, -item[item.Count - 1].Position.Tick);
                        }
                    }

                    g.ResetTransform();
                    g.ScaleTransform(1, -1);
                    moveToOrigin(g);
                    moveToHead(g, headTick);
                    moveToColumn(g, columnsCount);

                    // レーン分割線描画
                    using (var pen = new Pen(BackgroundColorProfile.LaneBorderColor, MeasurementProfile.BorderThickness))
                    {
                        for (int i = 0; i <= lanesCount; i++)
                        {
                            if (i % 2 != 0) continue;
                            float x = i * laneWidth;
                            g.DrawLine(pen, x, GetYPositionFromTick(headTick - paddingTick), x, GetYPositionFromTick(tailTick + paddingTick));
                        }
                    }

                    // 時間ガイドの描画
                    // そのイベントが含まれる小節(ただし[小節開始Tick, 小節開始Tick + 小節Tick)の範囲)からその拍子を適用

                    using (var beatPen = new Pen(BackgroundColorProfile.BeatLineColor, MeasurementProfile.BorderThickness))
                    using (var barPen = new Pen(BackgroundColorProfile.BarLineColor, MeasurementProfile.BorderThickness))
                    {
                        int headPos = 0;
                        int pos = 0;
                        for (int j = 0; j < sigs.Count; j++)
                        {
                            int barTick = (int)(TicksPerBeat * sigs[j].Value);
                            int beatTick = Math.Min(TicksPerBeat, barTick);

                            while (pos <= tailTick)
                            {
                                if (j < sigs.Count - 1 && pos - headPos >= (sigs[j + 1].Key - headPos) / barTick * barTick) break;
                                float y = GetYPositionFromTick(pos);
                                g.DrawLine((pos - headPos) % barTick == 0 ? barPen : beatPen, 0, y, wholeLaneWidth, y);
                                pos += beatTick;
                            }
                            headPos = pos;
                        }
                    }


                    // ノーツ描画
                    foreach (var hold in usingLongNoteQueues.ContainsKey('2') ? usingLongNoteQueues['2'] : emptyLong)
                    {
                        dc.DrawHoldBackground(new RectangleF(
                            (MeasurementProfile.UnitLaneWidth + MeasurementProfile.BorderThickness) * hold[0].Position.LaneIndex + MeasurementProfile.BorderThickness,
                            GetYPositionFromTick(hold[0].Position.Tick),
                            (MeasurementProfile.UnitLaneWidth + MeasurementProfile.BorderThickness) * hold[0].Position.Width - MeasurementProfile.BorderThickness,
                            GetYPositionFromTick(hold[1].Position.Tick - hold[0].Position.Tick)
                            ));
                    }

                    foreach (var slide in usingLongNoteQueues.ContainsKey('3') ? usingLongNoteQueues['3'] : emptyLong)
                    {
                        var visibleSteps = slide.Where(visibleStep).ToList();
                        for (int i = 0; i < slide.Count - 1; i++)
                        {
                            dc.DrawSlideBackground(
                                laneWidth * slide[i].Position.Width - MeasurementProfile.BorderThickness,
                                laneWidth * slide[i + 1].Position.Width - MeasurementProfile.BorderThickness,
                                laneWidth * slide[i].Position.LaneIndex,
                                GetYPositionFromTick(slide[i].Position.Tick),
                                laneWidth * slide[i + 1].Position.LaneIndex,
                                GetYPositionFromTick(slide[i + 1].Position.Tick) + 0.4f,
                                GetYPositionFromTick(visibleSteps.Last(p => p.Position.Tick <= slide[i].Position.Tick).Position.Tick),
                                GetYPositionFromTick(visibleSteps.First(p => p.Position.Tick >= slide[i + 1].Position.Tick).Position.Tick),
                                MeasurementProfile.ShortNoteHeight);
                        }
                    }

                    foreach (var airAction in usingLongNoteQueues.ContainsKey('4') ? usingLongNoteQueues['4'] : emptyLong)
                    {
                        dc.DrawAirHoldLine(
                            laneWidth * (airAction[0].Position.LaneIndex + airAction[0].Position.Width / 2f),
                            GetYPositionFromTick(airAction[0].Position.Tick),
                            GetYPositionFromTick(airAction[airAction.Count - 1].Position.Tick),
                            MeasurementProfile.ShortNoteHeight);
                    }

                    foreach (var hold in usingLongNoteQueues.ContainsKey('2') ? usingLongNoteQueues['2'] : emptyLong)
                    {
                        dc.DrawHoldBegin(GetRectFromNotePosition(hold[0].Position));
                        dc.DrawHoldEnd(GetRectFromNotePosition(hold[hold.Count - 1].Position));
                    }

                    foreach (var slide in usingLongNoteQueues.ContainsKey('3') ? usingLongNoteQueues['3'] : emptyLong)
                    {
                        dc.DrawSlideBegin(GetRectFromNotePosition(slide[0].Position));
                        foreach (var item in slide.Where(visibleStep).Skip(1))
                        {
                            if (item.Position.Tick < headTick - paddingTick) continue;
                            if (item.Position.Tick > tailTick + paddingTick) break;
                            dc.DrawSlideStep(GetRectFromNotePosition(item.Position));
                        }
                    }

                    // ロング終点AIR
                    var airs = usingShortNoteQueues.ContainsKey('5') ? usingShortNoteQueues['5'] : Enumerable.Empty<NoteDefinition>();
                    foreach (var air in airs)
                    {
                        if (!longEndsPos.Contains(air.Position)) continue;
                        dc.DrawAirStep(GetRectFromNotePosition(air.Position));
                    }

                    var shortNotesDic = usingShortNoteQueues['1'].GroupBy(p => p.Type).ToDictionary(p => p.Key, p => p.Select(q => q.Position));
                    void drawNotes(char key, Action<NotePosition> drawer)
                    {
                        if (!shortNotesDic.ContainsKey(key)) return;
                        foreach (var item in shortNotesDic[key]) drawer(item);
                    }

                    drawNotes('1', item => dc.DrawTap(GetRectFromNotePosition(item)));
                    drawNotes('2', item => dc.DrawExTap(GetRectFromNotePosition(item)));
                    drawNotes('5', item => dc.DrawExTap(GetRectFromNotePosition(item)));
                    drawNotes('6', item => dc.DrawExTap(GetRectFromNotePosition(item)));
                    drawNotes('3', item => dc.DrawFlick(GetRectFromNotePosition(item)));
                    drawNotes('4', item => dc.DrawDamage(GetRectFromNotePosition(item)));

                    foreach (var airAction in usingLongNoteQueues.ContainsKey('4') ? usingLongNoteQueues['4'] : emptyLong)
                    {
                        foreach (var item in airAction.Skip(1))
                        {
                            if (item.Position.Tick < headTick - paddingTick) continue;
                            if (item.Position.Tick > tailTick + paddingTick) break;
                            dc.DrawAirAction(GetRectFromNotePosition(item.Position).Expand(-MeasurementProfile.ShortNoteHeight * 0.28f));
                        }
                    }

                    foreach (var air in airs)
                    {
                        var vd = air.Type == '2' || air.Type == '5' || air.Type == '6' ? VerticalAirDirection.Down : VerticalAirDirection.Up;
                        var hd = air.Type == '1' || air.Type == '2' || air.Type == '7' ? HorizontalAirDirection.Center :
                            (air.Type == '3' || air.Type == '5' || air.Type == '8' ? HorizontalAirDirection.Left : HorizontalAirDirection.Right);
                        dc.DrawAir(GetRectFromNotePosition(air.Position), vd, hd);
                    }

                    g.ResetTransform();
                    moveToOrigin(g);
                    moveToHead(g, headTick);
                    moveToColumn(g, columnsCount);

                    // 小節番号
                    using (var brush = new SolidBrush(BackgroundColorProfile.BarIndexColor))
                    {
                        int pos = 0;
                        int barCount = 0;
                        for (int j = 0; j < sigs.Count; j++)
                        {
                            int currentBarTick = (int)(TicksPerBeat * sigs[j].Value);
                            for (int i = 0; pos + i * currentBarTick < tailTick; i++)
                            {
                                if (j < sigs.Count - 1 && i * currentBarTick >= (sigs[j + 1].Key - pos) / currentBarTick * currentBarTick) break;

                                int tick = pos + i * currentBarTick;
                                barCount++;
                                if (tick < headTick) continue;
                                var point = new PointF(-strSize.Width, -GetYPositionFromTick(tick) - strSize.Height);
                                g.DrawString(string.Format("{0:000}", barCount), MeasurementProfile.Font, brush, point);
                            }

                            if (j < sigs.Count - 1)
                                pos += (sigs[j + 1].Key - pos) / currentBarTick * currentBarTick;
                        }
                    }

                    float rightBaseX = wholeLaneWidth + strSize.Width / 3;

                    // BPM
                    using (var brush = new SolidBrush(BackgroundColorProfile.BpmColor))
                    {
                        foreach (var item in Score.BpmDefinitions.Where(p => p.Key >= headTick && p.Key < tailTick))
                        {
                            var point = new PointF(rightBaseX, -GetYPositionFromTick(item.Key) - strSize.Height);
                            g.DrawString(string.Format("{0:000.#}", item.Value), MeasurementProfile.Font, brush, point);
                        }
                    }

                    // 次の列に移動
                    headTick += columnTick;
                    columnsCount++;
                }
            }

            return bmp;
        }

        protected RectangleF GetRectFromNotePosition(NotePosition pos)
        {
            return new RectangleF(
                (MeasurementProfile.UnitLaneWidth + MeasurementProfile.BorderThickness) * pos.LaneIndex + MeasurementProfile.BorderThickness,
                GetYPositionFromTick(pos.Tick) - MeasurementProfile.ShortNoteHeight / 2,
                (MeasurementProfile.UnitLaneWidth + MeasurementProfile.BorderThickness) * pos.Width - MeasurementProfile.BorderThickness,
                MeasurementProfile.ShortNoteHeight
                );
        }

        protected float GetYPositionFromTick(int tick)
        {
            return tick * MeasurementProfile.UnitBeatHeight / TicksPerBeat;
        }
    }

    public class MeasurementProfile
    {
        public int UnitLaneWidth { get; set; }
        public int BorderThickness => (int)Math.Round(UnitLaneWidth * 0.1f);
        public int ShortNoteHeight { get; set; }
        public float UnitBeatHeight { get; set; }
        public float PaddingWidth { get; set; }
        public Func<int, int> CalcColumnTickFromTicksPerBeat { get; set; }
        public Func<int, int> CalcPaddingTickFromTicksPerBeat { get; set; }
        public Font Font { get; set; }
    }

    public class BackgroundColorProfile
    {
        public Color BackgroundColor { get; set; }
        public Color BarLineColor { get; set; }
        public Color BeatLineColor { get; set; }
        public Color BarIndexColor { get; set; }
        public Color LaneBorderColor { get; set; }
        public Color BpmColor { get; set; }
    }
}
