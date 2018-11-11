using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Ched.Drawing;

namespace Sus2Image.Converter
{
    public class Sus2Image
    {
        public MeasurementProfile MeasurementProfile { get; set; }
        public ColorProfile NoteColorProfile { get; set; }
        public BackgroundColorProfile BackgroundColorProfile { get; set; }

        public Sus2Image(StreamReader reader)
        {
        }

        public Image Convert()
        {
            throw new NotImplementedException();
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
