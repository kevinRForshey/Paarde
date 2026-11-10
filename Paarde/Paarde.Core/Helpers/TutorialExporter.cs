using System.Net;
using System.Text;

using Paarde.Core.Models;
using Paarde.Core.ViewModels;

namespace Paarde.Core.Helpers;

// Builds a self-contained Markdown or HTML document from a tutorial run's state, for the user to
// save and share outside the app. Mirrors what TutorialView/TutorialDetailControl actually
// show - the overview, and each file's steps numbered the same way TutorialStepViewModel.StepLabel
// numbers them - rather than anything app-internal (status messages, applied state, the snippet
// text that's only ever sent to the LLM, never shown in either view).
public static class TutorialExporter
{
    public static string BuildMarkdown(string tutorialOverview, IReadOnlyList<ProposedFileChangeViewModel> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Tutorial");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(tutorialOverview))
        {
            builder.AppendLine(tutorialOverview.Trim());
            builder.AppendLine();
        }

        foreach (var file in files)
        {
            builder.AppendLine($"## {file.FilePath}");
            builder.AppendLine();

            for (var i = 0; i < file.Hunks.Count; i++)
            {
                var hunk = file.Hunks[i];
                builder.AppendLine($"### Step {i + 1}: {hunk.Title}");
                builder.AppendLine();

                if (!string.IsNullOrWhiteSpace(hunk.Explanation))
                {
                    builder.AppendLine(hunk.Explanation.Trim());
                    builder.AppendLine();
                }
            }
        }

        return builder.ToString().TrimEnd() + "\n";
    }

    public static string BuildHtml(string tutorialOverview, IReadOnlyList<ProposedFileChangeViewModel> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html>");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<title>Tutorial</title>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<h1>Tutorial</h1>");

        if (!string.IsNullOrWhiteSpace(tutorialOverview))
        {
            AppendMarkdownAsHtml(builder, tutorialOverview);
        }

        foreach (var file in files)
        {
            builder.AppendLine($"<h2>{WebUtility.HtmlEncode(file.FilePath)}</h2>");

            for (var i = 0; i < file.Hunks.Count; i++)
            {
                var hunk = file.Hunks[i];
                builder.AppendLine($"<h3>Step {i + 1}: {WebUtility.HtmlEncode(hunk.Title)}</h3>");

                if (!string.IsNullOrWhiteSpace(hunk.Explanation))
                {
                    AppendMarkdownAsHtml(builder, hunk.Explanation);
                }
            }
        }

        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }

    // Reuses MarkdownInlineParser rather than a second parser, so overview/explanation text
    // renders consistently between the in-app MarkdownTextBlock and this export.
    private static void AppendMarkdownAsHtml(StringBuilder builder, string text)
    {
        var inList = false;

        foreach (var line in MarkdownInlineParser.Parse(text))
        {
            if (line.IsListItem && !inList)
            {
                builder.AppendLine("<ul>");
                inList = true;
            }
            else if (!line.IsListItem && inList)
            {
                builder.AppendLine("</ul>");
                inList = false;
            }

            var spansHtml = string.Concat(line.Spans.Select(RenderSpan));
            builder.AppendLine(line.IsListItem ? $"<li>{spansHtml}</li>" : $"<p>{spansHtml}</p>");
        }

        if (inList)
        {
            builder.AppendLine("</ul>");
        }
    }

    private static string RenderSpan(MarkdownSpan span)
    {
        var encoded = WebUtility.HtmlEncode(span.Text);

        return span.Kind switch
        {
            MarkdownSpanKind.Bold => $"<strong>{encoded}</strong>",
            MarkdownSpanKind.Italic => $"<em>{encoded}</em>",
            MarkdownSpanKind.Code => $"<code>{encoded}</code>",
            _ => encoded,
        };
    }
}
