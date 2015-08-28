/* 
 * This parser was stolen from somewhere and retouched to include [[[code]]] markup
 */
using System;
using System.Collections.Generic;
using System.Text;

namespace RoadkillWikiExtractor
{
    public delegate void LinkEventHandler(object sender, LinkEventArgs e);

    #region LinkEventArgs

    /// <summary>
    ///     Event fired when a link is processed, giving the caller the ability to translate
    /// </summary>
    public class LinkEventArgs : EventArgs
    {
        public enum TargetEnum
        {
            Internal,
            External,
            Unknown
        };

        public LinkEventArgs(string link, string href, string text, TargetEnum target)
        {
            Link = link;
            Href = href;
            Text = text;
            Target = target;
        }

        /// <summary>
        ///     Original Link
        /// </summary>
        public string Link { get; set; }

        /// <summary>
        ///     Mapped Link (in case of interwiki)
        ///     This is where the link will actually target
        /// </summary>
        public string Href { get; set; }

        public string Text { get; set; }

        /// <summary>
        ///     internal or external window
        /// </summary>
        public TargetEnum Target { get; set; }
    }

    #endregion

    /// <summary>
    ///     The Creole Parser is a .NET class which will translate Wiki Creole 1.0 (see http://wikicreole.org into HTML
    ///     This is fully 1.0 Wiki Creole Compliant
    ///     This class also supports the following creole additions:
    ///     1. __underline__        ==> &lt;u&gt;underlined&lt;/u&gt;
    ///     2. ^^super^^script      ==> &lt;sup&gt;super&lt;/sup&gt;script
    ///     3. ,,sub,,script        ==> &lt;sub&gt;sub&lt;/sub&gt;script
    ///     4. --strikethrough--    ==> &lt;del&gt;strikethrough&lt;/del&gt;
    ///     5. TAB chars are replaced with 7 &amp;nbsp; unless the TabStop is set to 0.
    ///     You can add Interwiki mappings by using the <see cref="InterWiki"/> collection
    ///     You can add additional HTML markup by adding entires to the <see cref="HtmlAttributes"/> collection
    ///     Ex:
    ///     parser.HTMLAttributes.Add("&lt;thead&gt;", "&lt;thead style=""border: solid=;""&gt;");
    ///     You if you define an event handler for OnLink you can modify the link that is generated
    /// </summary>
    public class CreoleParser
    {
        /// <summary>
        ///     This collection allows you to substitute markup with your own custom markup
        ///     Example:
        ///     HTMLAttributes.Add("&lt;h1&gt;", "&lt;h1 class id=""myH1Class""&gt;");
        /// </summary>
        public readonly Dictionary<string, string> HtmlAttributes;

        private int _nTabSpaces;
        private string _tabStop;

        /// <summary>
        ///     Interwiki dictionary
        ///     You can add interwiki links by adding the prefix to url mappings
        ///     Example:
        ///     InterWiki.Add("wikipedia", "http://wikipedia.org/{0}");
        ///     allows the user to do a wikipedia link like:
        ///     [[wikipedia:Microsoft]]
        /// </summary>
        public Dictionary<string, string> InterWiki;

        public CreoleParser()
        {
            HtmlAttributes = new Dictionary<string, string>();
            InterWiki = new Dictionary<string, string>();
            TabStop = 7; // default to 7 char tabstop
        }

        /// <summary>
        ///     Map \t into &amp;nbsp;
        ///     If you set this to 0 there will be no substitution.
        /// </summary>
        public int TabStop // number of characters for a tab
        {
            set
            {
                _nTabSpaces = value;
                var sb = new StringBuilder();
                for (var i = 0; i < _nTabSpaces; i++)
                    sb.Append("&nbsp;");
                _tabStop = sb.ToString();
            }
            get { return _nTabSpaces; }
        }

        /// <summary>
        ///     Event Handler fired when a link is processed
        /// </summary>
        public event LinkEventHandler OnLink;

        /// <summary>
        ///     Convert creole markup to HTML
        /// </summary>
        /// <param name="markup">creole markup</param>
        /// <returns>HTML</returns>
        public string ToHtml(string markup)
        {
            return _processAllMarkup(markup);
        }

        #region internal Process Creole methods

        private string _processAllMarkup(string markup)
        {
            // all of the syntax of flowing formatting markup across "soft" paragraph breaks gets a lot easier if we just merge soft breaks together.
            // This is what _mergeLines does, giving us an array of "lines" which can be processed for line based formatting such as ==, etc.
            List<int> originalLineNumbers;
            var lines = _breakMarkupIntoLines(markup, out originalLineNumbers);
            var htmlMarkup = new StringBuilder();

            htmlMarkup.Append(_getStartTag("<P>").Replace("<P", "<P id=\"CreoleLine0\"")); // start with paragraph

            var iBullet = 0; // bullet indentation level
            var iNumber = 0; // ordered list indentation level
            var inTable = false; // are in a table definition
            var inEscape = false; // we are in an escaped section
            var inCode = false; // we are in an code section
            var idParagraph = 1; // id for paragraphs

            // process each line of the markup, since bullets, numbers and tables depend on start of line
            foreach (var l in lines)
            {
                // make a copy as we will modify line into HTML as we go
                var line = l.Trim('\r');
                var lineTrimmed = line.TrimStart(' ');

                // if we aren't in an escaped section
                if (!inEscape && !inCode)
                {
                    // if we were in a table definition and this isn't another row 
                    if ((inTable) && (lineTrimmed.Length > 0) && lineTrimmed[0] != '|')
                    {
                        // then we close the table out
                        htmlMarkup.Append("</TABLE>");
                        inTable = false;
                    }

                    // process line based commands (aka, starts with a ** --> bulleted list) 

                    // ---  if we found a line completely empty, translate it as end of paragraph
                    if (lineTrimmed.Trim().Length == 0)
                    {
                        // close any pending lists
                        _closeLists(ref htmlMarkup, ref iBullet, ref iNumber, lineTrimmed);

                        // end of paragraph (NOTE: we id paragraphs for conveinence sake
                        htmlMarkup.Append(String.Format("</P>\n{0}",
                            _getStartTag("<p>").Replace("<P", String.Format("<P id=\"CreoleLine{0}\"", originalLineNumbers[idParagraph]))));
                    }
                    // --- process bullets
                    else switch (lineTrimmed[0])
                    {
                        case '*':
                            if (lineTrimmed.Length > 1 && lineTrimmed[1] == '*' && iBullet == 0)
                            {
                                // If we're not in a bulleted list, then this might be bold.
                                htmlMarkup.AppendLine(_processCreoleFragment(line));
                            }
                            else
                            {
                                // if we were doing an ordered list and this isn't one, we need to close the previous list down
                                if ((iNumber > 0) && lineTrimmed[0] != '#')
                                    htmlMarkup.Append(_closeList(ref iNumber, "</OL>"));

                                // generate correct indentation for bullets given current state of iBullet
                                htmlMarkup.Append(_processListIndentations(lineTrimmed, '*', ref iBullet, _getStartTag("<UL>"), "</UL>"));
                            }
                            break;
                        case '#':
                            // if we were doing an bullet list and this isn't one, we need to close the previous list down
                            if ((iBullet > 0) && lineTrimmed[0] != '*')
                                htmlMarkup.Append(_closeList(ref iBullet, "</UL>"));

                            // generate correct indentation for bullets given current state of iNumber
                            htmlMarkup.Append(_processListIndentations(lineTrimmed, '#', ref iNumber, _getStartTag("<OL>"), "</OL>"));
                            break;
                        default:
                            if (!inTable && lineTrimmed[0] == '=')
                            {
                                // close any pending lists
                                _closeLists(ref htmlMarkup, ref iBullet, ref iNumber, lineTrimmed);

                                // process = as headers only on start of lines
                                htmlMarkup.Append(_processCreoleFragment(_processHeadersCreole(line)));
                            }
                            // --- start of table
                            else if (!inTable && lineTrimmed.StartsWith("|="))
                            {
                                // start a new table

                                // close any pending lists
                                _closeLists(ref htmlMarkup, ref iBullet, ref iNumber, lineTrimmed);

                                inTable = true;
                                htmlMarkup.Append(_processTableHeaderRow(lineTrimmed));
                            }
                            // --- new row in table
                            else if (inTable && lineTrimmed[0] == '|')
                            {
                                // we are already processing table so this must be a new row
                                htmlMarkup.Append(_processTableRow(lineTrimmed));
                            }
                            // --- process {{{ }}} <pre>
                            else if (lineTrimmed.StartsWith("{{{") && (lineTrimmed.Length == 3))
                            {
                                // we are already processing table so this must be a new row
                                htmlMarkup.Append(_getStartTag("<PRE>"));
                                inEscape = true;
                            }
                            // --- process [[[ ]]] <code>
                            else if (lineTrimmed.StartsWith("[[[") && (lineTrimmed.Substring(3, 4) == "code"))
                            {
                                // we are already processing table so this must be a new row
                                htmlMarkup.Append(_getStartTag("<CODE>"));
                                inCode = true;
                            }
                            else
                            {
                                // we didn't find a special "start of line" command, 
                                // namely ordered list, unordered list or table definition

                                // just add it, processing any markup on it.
                                htmlMarkup.Append(String.Format("{0}\n", _processCreoleFragment(line)));
                            }
                            break;
                    }
                }
                else
                {
                    // we are looking for a line which starts with }}} to close off the preformated
                    if (lineTrimmed.StartsWith("}}}"))
                    {
                        htmlMarkup.Append("</PRE>\n");
                        inEscape = false;
                    }
                    else if (lineTrimmed.StartsWith("]]]"))
                    {
                        htmlMarkup.Append("</CODE>\n");
                        inCode = false;
                    }
                    else
                        if(inEscape)
                            htmlMarkup.Append(line + "\n"); // just pass it straight through unparsed
                        else 
                            htmlMarkup.Append(System.Web.HttpUtility.HtmlEncode(line) + "<BR />\n"); // just pass it straight through unparsed
                }
                idParagraph++;
            }
            // close out paragraph
            htmlMarkup.Append("</P>");

            // lastly, we want to expand tabs out into hard spaces so that the creole tabs are preserved 
            // NOTE: this is non-standard CREOLE...
            htmlMarkup = htmlMarkup.Replace("\t", _tabStop);

            // return the HTML we have generated 
            return htmlMarkup.ToString();
        }


        private static IEnumerable<string> _breakMarkupIntoLines(string markup, out List<int> originalLineNumbers)
        {
            originalLineNumbers = new List<int>();
            var lines = new List<string>();
            char[] chars = {'\n'};
            // break the creole into lines so we can process each line  
            var tempLines = markup.Split(chars);
            var inEscape = false; // we are in a preformated escape
            var inCode = false; // we are in a preformated escape
            // all markup works on a per line basis EXCEPT for the continuation of lines with simple CR, so we simply merge those in, which makes a 
            // much easier processing story later on
            for (var iLine = 0; iLine < tempLines.Length; iLine++)
            {
                var line = tempLines[iLine];
                var i = iLine + 1;

                if ((line.Length > 0) && (line != "\r") && line[0] != '=')
                {
                    if (!inEscape && !inCode)
                    {
                        if (line.StartsWith("{{{"))
                        {
                            inEscape = true;
                        }
                        else if (line.StartsWith("[[["))
                        {
                            inCode = true;
                        }
                        else
                        {
                            // merge all lines which don't start with a command line together
                            while (true)
                            {
                                if (i == tempLines.Length)
                                {
                                    iLine = i - 1;
                                    break;
                                }

                                var trimmedLine = tempLines[i].Trim();
                                if ((trimmedLine.Length == 0) ||
                                    trimmedLine[0] == '\r' ||
                                    trimmedLine[0] == '#' ||
                                    trimmedLine[0] == '*' ||
                                    trimmedLine.StartsWith("{{{") ||
                                    trimmedLine.StartsWith("[[[") ||
                                    trimmedLine[0] == '=' ||
                                    trimmedLine.StartsWith("----") ||
                                    trimmedLine[0] == '|')
                                {
                                    iLine = i - 1;
                                    break;
                                }
                                line += " " + trimmedLine; // erg, does CR == whitespace?
                                i++;
                            }
                        }
                    }
                    else
                    {
                        if (line.StartsWith("}}}"))
                            inEscape = false;
                        if (line.StartsWith("]]]"))
                            inCode = false;
                    }
                }
                // add the merged line to our list
                originalLineNumbers.Add(iLine);
                lines.Add(line);
            }
            originalLineNumbers.Add(lines.Count - 1);
            return lines;
        }

        private string _getStartTag(string tag)
        {
            return HtmlAttributes.ContainsKey(tag) ? HtmlAttributes[tag] : tag;
        }

        private void _closeLists(ref StringBuilder htmlMarkup, ref int iBullet, ref int iNumber, string lineTrimmed)
        {
            if (lineTrimmed.Length > 0)
            {
                // if we were doing an ordered list and this isn't one, we need to close the previous list down
                if ((iNumber > 0) && lineTrimmed[0] != '#')
                    htmlMarkup.Append(_closeList(ref iNumber, "</OL>"));

                // if we were doing an bullet list and this isn't one, we need to close the previous list down
                if ((iBullet > 0) && lineTrimmed[0] != '*')
                    htmlMarkup.Append(_closeList(ref iBullet, "</UL>"));
            }
            else
            {
                // if we were doing an ordered list and this isn't one, we need to close the previous list down
                if (iNumber > 0)
                    htmlMarkup.Append(_closeList(ref iNumber, "</OL>"));

                // if we were doing an bullet list and this isn't one, we need to close the previous list down
                if (iBullet > 0)
                    htmlMarkup.Append(_closeList(ref iBullet, "</UL>"));
            }
        }

        private string _processTableRow(string line)
        {
            var markup = _getStartTag("<TR>");
            var iPos = _indexOfWithSkip(line, "|", 0);
            while (iPos >= 0)
            {
                iPos += 1;
                var iEnd = _indexOfWithSkip(line, "|", iPos);
                if (iEnd >= iPos)
                {
                    var cell = _processCreoleFragment(line.Substring(iPos, iEnd - iPos)).Trim();
                    if (cell.Length == 0)
                        cell = "&nbsp;"; // table won't render if there isn't at least something...
                    markup += String.Format("{0}{1}</TD>", _getStartTag("<TD>"), cell);
                    iPos = iEnd;
                }
                else
                    break;
            }
            markup += "</TR>";
            return markup;
        }

        /// <summary>
        ///     passed a table definition line which starts with "|=" it outputs the start of a table definition
        /// </summary>
        /// <param name="line"></param>
        /// <returns></returns>
        private string _processTableHeaderRow(string line)
        {
            var markup = "";
            markup += _getStartTag("<TABLE>");
            // add header
            markup += String.Format("{0}\n{1}\n", _getStartTag("<THEAD>"), _getStartTag("<TR>"));

            // process each |= cell section
            var iPos = _indexOfWithSkip(line, "|=", 0);
            while (iPos >= 0)
            {
                var iEnd = _indexOfWithSkip(line, "|=", iPos + 1);
                string cell;
                if (iEnd > iPos)
                {
                    iPos += 2;
                    cell = line.Substring(iPos, iEnd - iPos);
                    iPos = iEnd;
                }
                else
                {
                    iPos += 2;
                    if (line.Length - iPos > 0)
                        cell = line.Substring(iPos).TrimEnd('|');
                    else
                        cell = "";
                    iPos = -1;
                }
                if (cell.Length == 0)
                    // forces the table cell to always be rendered
                    cell = "&nbsp;";

                // create cell entry
                markup += String.Format("{0}{1}</TD>", _getStartTag("<TD>"), _processCreoleFragment(cell));
            }
            // close up row and header
            markup += "</TR>\n</THEAD>\n";
            return markup;
        }

        private string _processListIndentations(string line, char indentMarker, ref int iCurrentIndent, string indentTag, string outdentTag)
        {
            var markup = "";
            var iNewIndent = 0;
            for (var i = 0; i < line.Length; i++)
            {
                if (line[i] == indentMarker)
                    iNewIndent++;
                else
                    break;
            }
            // strip off counters
            line = line.Substring(iNewIndent);

            // close down bullets if we have fewer *s
            while (iNewIndent < iCurrentIndent)
            {
                markup += String.Format("{0}\n", outdentTag);
                iCurrentIndent--;
            }

            // add bullets if we have more *s
            while (iNewIndent > iCurrentIndent)
            {
                markup += String.Format("{0}\n", indentTag);
                iCurrentIndent++;
            }
            // mark the line in the list, processing the inner fragment for any additional markup
            markup += String.Format("{0}{1}</LI>\n", _getStartTag("<LI>"), _processCreoleFragment(line));
            return markup;
        }

        /// <summary>
        ///     Given the current indentation level, close out the list
        /// </summary>
        /// <param name="iIndent"></param>
        /// <param name="closeHtml"></param>
        private string _closeList(ref int iIndent, string closeHtml)
        {
            var html = "";
            while (iIndent > 0)
            {
                html += string.Format("{0}\n", closeHtml);
                iIndent--;
            }
            return html;
        }

        private string _processFreeLink(string schema, string markup)
        {
            var iPos = _indexOfWithSkip(markup, schema, 0);
            while (iPos >= 0)
            {
                string href;
                var iEnd = _indexOfWithSkip(markup, " ", iPos);
                if (iEnd > iPos)
                {
                    href = markup.Substring(iPos, iEnd - iPos);
                    var anchor = String.Format("<A target=_blank href=\"{0}\">{0}</A>", href);
                    markup = markup.Substring(0, iPos) + anchor + markup.Substring(iEnd);
                    iPos = iPos + anchor.Length;
                }
                else
                {
                    href = markup.Substring(iPos);
                    markup = markup.Substring(0, iPos) + String.Format("<A target=_blank href=\"{0}\">{0}</A>", href);
                    break;
                }
                iPos = _indexOfWithSkip(markup, schema, iPos);
            }
            return markup;
        }

        /// <summary>
        ///     Process http:, https: ftp: links automatically into a hrefs
        /// </summary>
        /// <param name="markup"></param>
        /// <returns></returns>
        private string _processFreeLinks(string markup)
        {
            markup = _processFreeLink("ftp:", markup);
            markup = _processFreeLink("http:", markup);
            return _processFreeLink("https:", markup);
        }

        private string _stripTildeEscapeCreole(string markup)
        {
            var iPos = markup.IndexOf('~');
            while ((iPos >= 0) && (iPos < markup.Length - 2))
            {
                var token = markup.Substring(iPos, 2);
                if (token == "~~")
                {
                    markup = markup.Substring(0, iPos) + "~" + markup.Substring(iPos + 2);
                    iPos++;
                }
                else
                {
                    // if a non-whitespace char follows, we want to strip it
                    if (token.Trim().Length != 1)
                    {
                        markup = markup.Remove(iPos, 1);
                    }
                }
                iPos = markup.IndexOf('~', iPos);
            }
            return markup;
        }

        /// <summary>
        ///     Process a fragment of markup
        /// </summary>
        /// <param name="fragment">fragment</param>
        /// <returns></returns>
        private string _processCreoleFragment(string fragment)
        {
            fragment = _processBoldCreole(fragment);
            fragment = _processItalicCreole(fragment);
            fragment = _processUnderlineCreole(fragment);
            fragment = _processSuperscriptCreole(fragment);
            fragment = _processSubscriptCreole(fragment);
            fragment = _processStrikethroughCreole(fragment);
            fragment = _processLineBreakCreole(fragment);
            fragment = _processHorzRuleCreole(fragment);
            fragment = _processFreeLinks(fragment);
            fragment = _processImageCreole(fragment);
            fragment = _processLinkCreole(fragment);
            fragment = _stripEscapeCreole(fragment);
            fragment = _stripTildeEscapeCreole(fragment);
            return fragment;
        }

        /// <summary>
        ///     Helper function to get the index of a match but skipping the content of
        ///     bracketed tags which can have creole inside it.
        ///     It is used just like string.IndexOf()
        ///     Using this allows creole inside of links and images.  It's not clear from the spec what is expected
        ///     but it seems desirable to allow this stuff to be nested to the degree we can do it.
        ///     [[Link|**bold**]]
        ///     [[link|{{foo.jpg|test}}]]
        /// </summary>
        /// <param name="markup">creole</param>
        /// <param name="match">token</param>
        /// <param name="iPos">starting position</param>
        /// <returns>index of token</returns>
        private int _indexOfWithSkip(string markup, string match, int iPos)
        {
            var fSkipLink = (match != "[[") && (match != "]]");
            var fSkipEscape = (match != "{{{") && (match != "}}}");
            var fSkipCode = (match != "[[[") && (match != "]]]");
            var fSkipImage = (match != "{{") && (match != "}}");

            var tokenLength = match.Length;
            if (tokenLength < 3)
                tokenLength = 3; // so we can match on {{{
            for (var i = 0; i <= markup.Length - match.Length; i++)
            {
                if ((markup.Length - i) < tokenLength)
                    tokenLength = markup.Length - i;
                var token = markup.Substring(i, tokenLength);
                if (fSkipEscape && token.StartsWith("{{{"))
                {
                    // skip escape
                    var iEnd = markup.IndexOf("}}}", i, StringComparison.Ordinal);
                    if (iEnd > 0)
                    {
                        i = iEnd + 2; // plus for loop ++
                        continue;
                    }
                }
                if (fSkipCode && token.StartsWith("[[["))
                {
                    // skip code
                    var iEnd = markup.IndexOf("]]]", i, StringComparison.Ordinal);
                    if (iEnd > 0)
                    {
                        i = iEnd + 2; // plus for loop ++
                        continue;
                    }
                }
                if (fSkipLink && token.StartsWith("[["))
                {
                    // skip link
                    var iEnd = markup.IndexOf("]]", i, StringComparison.Ordinal);
                    if (iEnd > 0)
                    {
                        i = iEnd + 1; // plus for loop ++
                        continue;
                    }
                }
                if (fSkipImage && token.StartsWith("{{"))
                {
                    // skip image
                    var iEnd = markup.IndexOf("}}", i, StringComparison.Ordinal);
                    if (iEnd > 0)
                    {
                        i = iEnd + 1; // plus for loop ++
                        continue;
                    }
                }
                if (token.StartsWith(match))
                {
                    // make sure previous char is not a ~, for this we have to go back 2 chars as double ~ is an escaped escape char
                    if (i > 2)
                    {
                        var tildeCheck = markup.Substring(i - 2, 2);
                        if ((tildeCheck != "~~") && (tildeCheck[1] == '~'))
                            continue; // then we don't want to match this...it's been escaped
                    }

                    // only if it starts past our starting point
                    if (i >= iPos)
                        return i;
                }
            }
            return -1;
        }

        /// <summary>
        ///     Processes bracketing creole markup into HTML
        ///     **foo** into <b>foo</b> etc..
        /// </summary>
        /// <param name="match">bracketing token Ex: "=="</param>
        /// <param name="startTag">start tag to replace it with Ex: "h1"</param>
        /// <param name="endTag">end tag to replace it with Ex: "h1"</param>
        /// <param name="markup">creole markup</param>
        /// <returns>markup with bracketing tokens replaces with HTML</returns>
        private string _processBracketingCreole(string match, string startTag, string endTag, string markup)
        {
            // look for a start 
            var iPos = _indexOfWithSkip(markup, match, 0);
            while (iPos >= 0)
            {
                // special case for italics and urls, if match = // we don't match if previous char is a url
                if ((match == "//") &&
                    (((iPos >= 6) && (markup.Substring(iPos - 6, 6).ToLower() == "https:")) ||
                     ((iPos >= 5) && (markup.Substring(iPos - 5, 5).ToLower() == "http:")) ||
                     ((iPos >= 4) && (markup.Substring(iPos - 4, 4).ToLower() == "ftp:"))))
                {
                    // skip it, it's a url (I think)
                    iPos = _indexOfWithSkip(markup, match, iPos + 1);
                    continue;
                }

                var iEnd = _indexOfWithSkip(markup, match, iPos + match.Length);
                if (iEnd > 0)
                {
                    var markedText = markup.Substring(iPos + match.Length, iEnd - (iPos + match.Length));
                    if (markedText.Length > 0)
                    {
                        // add previous + start tag + markedText + end Tag + end
                        markup = markup.Substring(0, iPos)
                                 + startTag
                                 + markedText
                                 + endTag
                                 + markup.Substring(iEnd + match.Length);
                    }
                    iPos = _indexOfWithSkip(markup, match, iEnd + 1);
                }
                else
                {
                    var markedText = markup.Substring(iPos + match.Length);
                    // treat end of line as end of bracketing
                    markup = markup.Substring(0, iPos)
                             + startTag
                             + markedText
                             + endTag;
                    break;
                }
            }
            return markup;
        }

        /// <summary>
        ///     Process Headers Creole into HTML
        /// </summary>
        /// <param name="markup"></param>
        /// <returns></returns>
        private string _processHeadersCreole(string markup)
        {
            markup = _processBracketingCreole("======", _getStartTag("<H6>"), "</H6>", markup);
            markup = _processBracketingCreole("=====", _getStartTag("<H5>"), "</H5>", markup);
            markup = _processBracketingCreole("====", _getStartTag("<H4>"), "</H4>", markup);
            markup = _processBracketingCreole("===", _getStartTag("<H3>"), "</H3>", markup);
            markup = _processBracketingCreole("==", _getStartTag("<H2>"), "</H2>", markup);
            markup = _processBracketingCreole("=", _getStartTag("<H1>"), "</H1>", markup);
            return markup;
        }

        private string _processBoldCreole(string markup)
        {
            return _processBracketingCreole("**", _getStartTag("<STRONG>"), "</STRONG>", markup);
        }

        private string _processItalicCreole(string markup)
        {
            return _processBracketingCreole("//", _getStartTag("<EM>"), "</EM>", markup);
        }

        private string _processUnderlineCreole(string markup)
        {
            return _processBracketingCreole("__", _getStartTag("<U>"), "</U>", markup);
        }

        private string _processSuperscriptCreole(string markup)
        {
            return _processBracketingCreole("^^", _getStartTag("<SUP>"), "</SUP>", markup);
        }

        private string _processSubscriptCreole(string markup)
        {
            return _processBracketingCreole(",,", _getStartTag("<SUB>"), "</SUB>", markup);
        }

        private string _processStrikethroughCreole(string markup)
        {
            return _processBracketingCreole("--", _getStartTag("<DEL>"), "</DEL>", markup);
        }

        /// <summary>
        ///     Processes link markup into HTML
        /// </summary>
        /// <param name="markup">markup</param>
        /// <returns>markup with [[foo]] translated into &lt;a href&gt;&lt;/a&gt;</returns>
        private string _processLinkCreole(string markup)
        {
            var iPos = _indexOfWithSkip(markup, "[[", 0);
            while (iPos >= 0)
            {
                var iEnd = _indexOfWithSkip(markup, "]]", iPos);
                if (iEnd > iPos)
                {
                    iPos += 2;
                    // get the contents of the cell
                    var cell = markup.Substring(iPos, iEnd - iPos);
                    var link = cell;
                    var href = cell; //default to assuming it's the href
                    var text = href; // as well as the text
                    var iSplit = cell.IndexOf('|'); // unless of course there is a splitter
                    if (iSplit > 0)
                    {
                        // href is the front
                        href = cell.Substring(0, iSplit);
                        link = href;

                        // text is the creole processed fragment left over
                        text = _processCreoleFragment(cell.Substring(iSplit + 1));
                    }

                    // handle interwiki links
                    iSplit = href.IndexOf(':');
                    if (iSplit > 0)
                    {
                        var scheme = href.Substring(0, iSplit);
                        if (InterWiki.ContainsKey(scheme))
                        {
                            href = InterWiki[scheme] + href.Substring(iSplit + 1);
                        }
                    }
                    // default to external
                    var linkEventArgs = new LinkEventArgs(link, href, text, LinkEventArgs.TargetEnum.External);
                    if (OnLink != null)
                        OnLink(this, linkEventArgs);

                    markup = markup.Substring(0, iPos - 2)
                             + String.Format("<A href=\"{0}\" {2} {3}>{1}</A>",
                                 linkEventArgs.Href,
                                 linkEventArgs.Text,
                                 (linkEventArgs.Target == LinkEventArgs.TargetEnum.External) ? "target=_blank " : "",
                                 (linkEventArgs.Target == LinkEventArgs.TargetEnum.Unknown)
                                     ? "style='border-bottom:1px dashed #000000; text-decoration:none'"
                                     : "")
                             + markup.Substring(iEnd + 2);
                }
                else
                    break;
                iPos = _indexOfWithSkip(markup, "[[", iPos);
            }
            return markup;
        }

        /// <summary>
        ///     Process line break creole into <br />
        /// </summary>
        /// <param name="markup"></param>
        /// <returns></returns>
        private string _processLineBreakCreole(string markup)
        {
            var iPos = _indexOfWithSkip(markup, "\\\\", 0);
            while (iPos >= 0)
            {
                markup = markup.Substring(0, iPos) + _getStartTag("<BR />") + markup.Substring(iPos + 2);
                iPos = _indexOfWithSkip(markup, "\\\\", iPos);
            }
            return markup;
        }

        /// <summary>
        ///     Process horz rulle creole into <hr />
        /// </summary>
        /// <param name="markup"></param>
        /// <returns></returns>
        private string _processHorzRuleCreole(string markup)
        {
            if (markup.StartsWith("----"))
                return _getStartTag("<HR />") + markup.Substring(4);
            return markup;
        }

        /// <summary>
        ///     Process image creole into <img> </img>
        /// </summary>
        /// <param name="markup"></param>
        /// <returns></returns>
        private string _processImageCreole(string markup)
        {
            var iPos = _indexOfWithSkip(markup, "{{", 0);
            while (iPos >= 0)
            {
                var iEnd = _indexOfWithSkip(markup, "}}", iPos);
                if (iEnd > iPos)
                {
                    iPos += 2;
                    var innards = markup.Substring(iPos, iEnd - iPos);
                    var href = innards;
                    var text = href;
                    var iSplit = innards.IndexOf('|');
                    if (iSplit > 0)
                    {
                        href = innards.Substring(0, iSplit);
                        text = _processCreoleFragment(innards.Substring(iSplit + 1));
                    }

                    markup = markup.Substring(0, iPos - 2)
                             + String.Format("<IMG src='{0}' alt='{1}'/>", href, text)
                             + markup.Substring(iEnd + 2);
                }
                else
                    break;
                iPos = _indexOfWithSkip(markup, "{{", iPos);
            }
            return markup;
        }

        /// <summary>
        ///     Remove escape creole (no longer needed after all other transforms are done
        /// </summary>
        /// <param name="markup"></param>
        /// <returns></returns>
        private string _stripEscapeCreole(string markup)
        {
            var iPos = markup.IndexOf("{{{", StringComparison.Ordinal);
            while (iPos >= 0)
            {
                var iEnd = markup.IndexOf("}}}", iPos, StringComparison.Ordinal);
                if (iEnd > iPos)
                {
                    markup = markup.Substring(0, iPos) + _getStartTag("<TT>") +
                             markup.Substring(iPos + 3, iEnd - (iPos + 3)) +
                             String.Format("</TT>") +
                             markup.Substring(iEnd + 3);

                    iPos = markup.IndexOf("{{{", iPos, StringComparison.Ordinal);
                }
                else
                    break;
            }
            return markup;
        }

        #endregion
    }
}