namespace JustModIt.Manager.Host.Tests.TestSupport;

internal static class RepositoryFiles
{
    public static string Root { get; } = FindRoot();

    public static string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>The project folders of the apps: the ones with a Product.props.</summary>
    public static IEnumerable<string> Apps() =>
        Directory.EnumerateDirectories(PathOf("src")).Where(project => File.Exists(Path.Combine(project, "Product.props")));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.EnumerateFiles("*.slnx").Any())
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No solution file above {AppContext.BaseDirectory}.");
    }
}
