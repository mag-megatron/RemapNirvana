using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvaloniaUI.Models
{
    public class InputMapping
    {
        public required string InputName { get; set; }
        public required string MappedTo { get; set; }
    }
}