using System.ComponentModel.DataAnnotations;

namespace BMSBT.ViewModels
{
    public class UpdateProfileViewModel
    {
        [Required]
        public string CurrentPassword { get; set; } = "";

        [Required]
        [MinLength(6)]
        public string NewPassword { get; set; } = "";

        [Required]
        [MinLength(6)]
        public string ConfirmPassword { get; set; } = "";
    }
}
