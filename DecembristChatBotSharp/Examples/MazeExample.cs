using DecembristChatBotSharp.Service;

namespace DecembristChatBotSharp.Examples;

/// <summary>
/// Example demonstrating how to use MazeRendererService
/// </summary>
public static class MazeExample
{
    public static void GenerateMazeImages(MazeRendererService renderer)
    {
        // Generate maze without solution
        var mazePath = renderer.GenerateAndSaveMaze("./mazes", withSolution: false);
        Console.WriteLine($"Maze saved to: {mazePath}");

        // Generate maze with solution
        var mazeWithSolutionPath = renderer.GenerateAndSaveMaze("./mazes", withSolution: true);
        Console.WriteLine($"Maze with solution saved to: {mazeWithSolutionPath}");

        // Get maze as byte array (for sending via Telegram)
        var imageBytes = renderer.RenderMazeAsPng();
        Console.WriteLine($"Generated maze image: {imageBytes.Length} bytes");
    }
}

