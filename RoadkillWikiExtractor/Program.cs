using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using RoadkillWikiExtractor.ProgressBar;

namespace RoadkillWikiExtractor
{
    static class Program
    {
        static readonly CreoleParser Parser = new CreoleParser();

        static void Main(string[] args)
        {
            var options = new Options();
            if (CommandLine.Parser.Default.ParseArguments(args, options))
            {
                if (options.Verbose) Console.WriteLine("ConnectionString: {0}", options.ConnectionString);
                if (options.Verbose) Console.WriteLine("Output: {0}", options.OutputDirectory);
                MainAsync(options.OutputDirectory, options.ConnectionString, options.Database).Wait();
            }
            else
            {
                Console.WriteLine(options.GetUsage());
            }

        }



        static async Task MainAsync(string baseDirectory, string connectionString, string dbName)
        {
            var creoleDir = Path.Combine(baseDirectory, "Creole");
            var htmlDir = Path.Combine(baseDirectory, "Html");

            PrepareDirectories(baseDirectory, creoleDir, htmlDir);

            var client = new MongoClient(connectionString);
            var database = client.GetDatabase(dbName);
            var pagesCollection = database.GetCollection<BsonDocument>("Page");
            var pagesContentCollection = database.GetCollection<BsonDocument>("PageContent");

            AbstractBar bar = new SwayBar();

            var documents = await pagesCollection.Find(new BsonDocument()).ToListAsync();

            foreach (var document in documents)
            {
                var title = document.GetValue("Title").AsString;
                var filter = Builders<BsonDocument>.Filter.Eq("Page", document);
                await pagesContentCollection.Find(filter).ForEachAsync(d => CreateFile(creoleDir, htmlDir, title, d));
                bar.Step();
            }
        }

        private static void PrepareDirectories(string baseDirectory, string creoleDir, string htmlDir)
        {
            if (Directory.Exists(baseDirectory))
            {
                foreach (var file in Directory.GetFiles(baseDirectory)) File.Delete(file);
                if (Directory.Exists(creoleDir))
                    foreach (var file in Directory.GetFiles(creoleDir)) File.Delete(file);
                else
                    Directory.CreateDirectory(creoleDir);

                if (Directory.Exists(htmlDir))
                    foreach (var file in Directory.GetFiles(htmlDir)) File.Delete(file);
                else
                    Directory.CreateDirectory(htmlDir);
            }
            else
            {
                Directory.CreateDirectory(baseDirectory);
                Directory.CreateDirectory(creoleDir);
                Directory.CreateDirectory(htmlDir);
            }
        }

        private async static Task CreateFile(string creoleDir, string htmlDir, string title, BsonDocument d)
        {
            var text = d.GetValue("Text").AsString;
            
            using (var stream = File.Create(Path.Combine(creoleDir, title + ".creole")))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
            using (var stream = File.Create(Path.Combine(htmlDir, title + ".html")))
            {
                var html = Parser.ToHtml(text);
                var bytes = Encoding.UTF8.GetBytes(html);
                await stream.WriteAsync(bytes, 0, bytes.Length);
                
            }
        }

    }

}
