using Refit;

namespace WheelWizard.MiiImages.Domain;

public interface IMiiIMagesApi
{
    [Get("/miis/image.png")]
    Task<Stream> GetImageAsync(
        string data,
        string type,
        string expression,
        int width,
        int characterXRotate = 0,
        int characterYRotate = 0,
        int characterZRotate = 0,
        string bgColor = "FFFFFF00",
        int instanceCount = 1,
        int cameraXRotate = 0,
        int cameraYRotate = 0,
        int cameraZRotate = 0
    );
}
