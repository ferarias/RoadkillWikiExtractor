using System.IO;

namespace RoadkillWikiExtractor
{
    public static class FileExtensions
    {
        public static void EmptyDir(string d)
        {
            var directory = new DirectoryInfo(d);

            foreach (var file in directory.GetFiles()) file.Delete();
            foreach (var subDirectory in directory.GetDirectories()) subDirectory.Delete(true);
        }
    }
}
