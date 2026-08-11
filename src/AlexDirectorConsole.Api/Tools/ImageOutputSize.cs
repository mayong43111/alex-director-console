namespace AlexDirectorConsole.Api.Tools;

internal static class ImageOutputSize
{
    public static string Resolve(string imagePurpose, string projectImageSize) =>
        imagePurpose.Trim().Equals("project-frame", StringComparison.OrdinalIgnoreCase)
            ? projectImageSize
            : "1024x1024";
}