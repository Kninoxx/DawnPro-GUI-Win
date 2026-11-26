namespace DawnPro.Core;

public interface IDawnProReports
{
    void Write(byte[] report);
    byte[] Read();
}
