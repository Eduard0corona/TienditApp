namespace FoodParkDigital.Application.Orders.DTOs;

public class UploadedFileDto
{
    public string Url { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
}
