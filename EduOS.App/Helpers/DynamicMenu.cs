using System.Collections.Generic;

namespace EduOS.App.Helpers
{
    public class DynamicMenu
    {
        public static List<string> GetMenu()
        {
            return new List<string>
            {
                "Dashboard",
                "Students",
                "Teachers",
                "Classes",
                "Courses",
                "Finance",
                "HR",
                "Inventory",
                "Reports",
                "Settings"
            };
        }
    }
}
