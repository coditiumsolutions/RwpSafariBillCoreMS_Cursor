namespace BMSBT.ViewModels
{
    public class ProfileViewModel
    {
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public string? EmployeeId { get; set; }
        public string? Role { get; set; }
        public string? LoginTime { get; set; }
    }
}
