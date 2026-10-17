# TypeSafe image sample

This console sample sends an image to the `jev-preview` model, prints the
bounding-box answers, and writes a copy with the returned boxes outlined in red.
The SDK requires the `TypeSafe-Danger-Zone-Images: true` header for image requests.

Set `TYPESAFE_API_KEY`, then run with the input image and output image paths:

```powershell
$env:TYPESAFE_API_KEY = "your-api-key"
dotnet run --project samples/TypeSafe.Sample.Images -- photo.jpg photo-boxes.jpg
```

The sample accepts PNG or JPEG images and asks example questions about people,
vehicles, and the most prominent object. Update the question dictionary in
`Program.cs` to target other objects.