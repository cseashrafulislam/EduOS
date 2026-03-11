using System.Collections.Generic;

namespace EduOS.App.Helpers
{
    public static class MenuBuilder
    {
        public static List<string> GetMenus()
        {
            return new List<string>
            {
                "Students",
                "Teachers",
               "Classes",
                "Subjects",
                "Courses",
                "Finance",
                "HR",
                "Inventory"
            };
        }
    }
}
