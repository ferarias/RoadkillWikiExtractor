using CommandLine;
using CommandLine.Text;

namespace RoadkillWikiExtractor
{
    class Options
    {
        [Option('c', "connection", Required = true, HelpText = "MongoDB connection string.")]
        public string ConnectionString { get; set; }
        [Option('d', "database", Required = true, HelpText = "Database name.")]
        public string Database { get; set; }
        [Option('o', "output", Required = true, HelpText = "Output directory.")]
        public string OutputDirectory { get; set; }
        [ParserState]
        public IParserState LastParserState { get; set; }

        [Option(DefaultValue = true, HelpText = "Prints all messages to standard output.")]
        public bool Verbose { get; set; }

        [HelpOption]
        public string GetUsage()
        {
            return HelpText.AutoBuild(this, current => HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }
}
