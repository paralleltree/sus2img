using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

using Ched.Drawing;
using Sus2Image.Converter;

namespace Sus2Image.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScoreController : ControllerBase
    {
        [HttpPost]
        [Route("convert")]
        [ResponseCache(Location = ResponseCacheLocation.None, Duration = 0)]
        public IActionResult ConvertSusToImage()
        {
            var noteColorProfile = new ColorProfile()
            {
                BorderColor = new GradientColor(Color.FromArgb(160, 160, 160), Color.FromArgb(208, 208, 208)),
                TapColor = new GradientColor(Color.FromArgb(138, 0, 0), Color.FromArgb(255, 128, 128)),
                ExTapColor = new GradientColor(Color.FromArgb(204, 192, 0), Color.FromArgb(255, 236, 68)),
                FlickColor = Tuple.Create(new GradientColor(Color.FromArgb(68, 68, 68), Color.FromArgb(186, 186, 186)), new GradientColor(Color.FromArgb(0, 96, 138), Color.FromArgb(122, 216, 252))),
                DamageColor = new GradientColor(Color.FromArgb(8, 8, 116), Color.FromArgb(22, 40, 180)),
                HoldColor = new GradientColor(Color.FromArgb(196, 86, 0), Color.FromArgb(244, 156, 102)),
                HoldBackgroundColor = new GradientColor(Color.FromArgb(196, 166, 44, 168), Color.FromArgb(196, 216, 216, 0)),
                SlideColor = new GradientColor(Color.FromArgb(0, 16, 138), Color.FromArgb(86, 106, 255)),
                SlideLineColor = Color.FromArgb(196, 0, 214, 192),
                SlideBackgroundColor = new GradientColor(Color.FromArgb(196, 166, 44, 168), Color.FromArgb(196, 0, 164, 146)),
                AirUpColor = Color.FromArgb(28, 206, 22),
                AirDownColor = Color.FromArgb(192, 21, 216),
                AirActionColor = new GradientColor(Color.FromArgb(146, 0, 192), Color.FromArgb(212, 92, 255)),
                AirHoldLineColor = Color.FromArgb(216, 0, 196, 0),
                AirStepColor = new GradientColor(Color.FromArgb(6, 180, 10), Color.FromArgb(80, 224, 64))
            };
            var backgroundColorProfile = new BackgroundColorProfile()
            {
                BackgroundColor = Color.Black,
                BarLineColor = Color.FromArgb(160, 160, 160),
                BeatLineColor = Color.FromArgb(80, 80, 80),
                LaneBorderColor = Color.FromArgb(60, 60, 60),
                BarIndexColor = Color.White,
                BpmColor = Color.FromArgb(0, 192, 0)
            };
            var measurementProfile = new MeasurementProfile()
            {
                UnitLaneWidth = 4,
                ShortNoteHeight = 4,
                PaddingWidth = 30,
                UnitBeatHeight = 48,
                Font = new Font("MS Gothic", 8),
                CalcColumnTickFromTicksPerBeat = tpb => tpb * 12,
                CalcPaddingTickFromTicksPerBeat = tpb => tpb / 8
            };

            var file = Request.Form.Files["susFile"];
            using (var reader = new StreamReader(file.OpenReadStream()))
            using (var stream = new MemoryStream())
            {
                try
                {
                    var converter = new Converter.Sus2Image(reader)
                    {
                        NoteColorProfile = noteColorProfile,
                        BackgroundColorProfile = backgroundColorProfile,
                        MeasurementProfile = measurementProfile
                    };
                    converter.Convert().Save(stream, ImageFormat.Png);
                    return File(stream.ToArray(), "image/png");
                }
                catch
                {
                    return BadRequest(new { error = "Invalid sus file." });
                }
            }
        }
    }
}
