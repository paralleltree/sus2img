using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Sus2Image.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScoreController : ControllerBase
    {
        [HttpPost]
        [Route("convert")]
        public IActionResult ConvertSusToImage()
        {
            return StatusCode(501);
        }
    }
}
