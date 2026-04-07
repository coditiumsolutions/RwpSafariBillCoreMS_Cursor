using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMSBT.Models;

public partial class Configuration
{
    public int Uid { get; set; }

    public int ConfigId { get; set; }

    public string? ConfigKey { get; set; }

    [StringLength(1000, ErrorMessage = "ConfigValue cannot exceed 1000 characters.")]
    public string? ConfigValue { get; set; }
}
