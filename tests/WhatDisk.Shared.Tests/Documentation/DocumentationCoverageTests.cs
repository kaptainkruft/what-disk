using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace WhatDisk.Tests.Documentation;

/// <summary>
/// Enforces the project rule that every type and member, whatever its accessibility, has an XML documentation comment.
/// The compiler's own check (CS1591) only covers publicly visible members.
/// </summary>
public sealed class DocumentationCoverageTests
{
    /// <summary>The top-level folders that contain hand-written sources.</summary>
    private static readonly string[] SourceFolders = ["src", "tests"];

    /// <summary>
    /// Every type, member, field and enum value in the repository's C# sources is documented.
    /// </summary>
    [Fact]
    public void EveryTypeAndMemberHasADocumentationComment()
    {
        var root = FindRepositoryRoot();
        var missing = new List<string>();
        var checkedCount = 0;
        foreach (var file in EnumerateSources(root))
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file), new CSharpParseOptions(LanguageVersion.Latest), file, cancellationToken: cancellationToken);
            foreach (var member in tree.GetRoot(cancellationToken).DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                if (member is BaseNamespaceDeclarationSyntax or GlobalStatementSyntax)
                {
                    continue;
                }

                checkedCount++;
                if (!HasDocumentation(member))
                {
                    var line = member.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    missing.Add($"{Path.GetRelativePath(root, file)}:{line} {Describe(member)}");
                }
            }
        }

        Assert.True(checkedCount > 500, $"Only {checkedCount} declarations were found; the source search is probably wrong.");
        Assert.True(missing.Count == 0, $"Undocumented declarations:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}");
    }

    /// <summary>
    /// Finds the repository root by looking for the solution file above the test output directory.
    /// </summary>
    /// <returns>The repository root.</returns>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "WhatDisk.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (containing WhatDisk.slnx) was not found.");
    }

    /// <summary>
    /// Lists the hand-written C# sources: everything under <c>src</c> and <c>tests</c> except build output.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <returns>The source file paths.</returns>
    private static IEnumerable<string> EnumerateSources(string root) =>
        SourceFolders
            .Select(folder => Path.Join(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(static path => !path.Split(Path.DirectorySeparatorChar).Any(static segment => segment is "obj" or "bin"));

    /// <summary>
    /// Determines whether a declaration is preceded by a documentation comment.
    /// </summary>
    /// <param name="member">The declaration.</param>
    /// <returns><see langword="true"/> when it is documented.</returns>
    private static bool HasDocumentation(MemberDeclarationSyntax member) =>
        member.GetLeadingTrivia().Any(static trivia =>
            trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

    /// <summary>
    /// Describes a declaration for the failure message.
    /// </summary>
    /// <param name="member">The declaration.</param>
    /// <returns>A short description.</returns>
    private static string Describe(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax type => $"type {type.Identifier.Text}",
        MethodDeclarationSyntax method => $"method {method.Identifier.Text}",
        PropertyDeclarationSyntax property => $"property {property.Identifier.Text}",
        FieldDeclarationSyntax field => $"field {string.Join(", ", field.Declaration.Variables.Select(static variable => variable.Identifier.Text))}",
        ConstructorDeclarationSyntax constructor => $"constructor {constructor.Identifier.Text}",
        EnumMemberDeclarationSyntax value => $"enum value {value.Identifier.Text}",
        EventDeclarationSyntax @event => $"event {@event.Identifier.Text}",
        EventFieldDeclarationSyntax eventField => $"event {string.Join(", ", eventField.Declaration.Variables.Select(static variable => variable.Identifier.Text))}",
        DelegateDeclarationSyntax @delegate => $"delegate {@delegate.Identifier.Text}",
        _ => member.Kind().ToString(),
    };
}
