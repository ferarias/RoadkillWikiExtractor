# RoadkillWikiExtractor
A simple extractor of content from RoadkillWiki

This project is intendend to extract page and contents from a RoadKill Wiki installation.
Currently it only extracts MongoDB data.

To use, compile and invoke in a command line

`
RoadkillWikiExtractor --connection mongodb://<user>:<pass>@<mongodbhost> --database <database> --output <outputFolder>
`
