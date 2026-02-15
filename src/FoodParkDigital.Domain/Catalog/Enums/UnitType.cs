namespace FoodParkDigital.Domain.Catalog.Enums;

public enum UnitType
{
    // General
    Unit = 0,
    Pack = 1,
    Box = 2,
    Pair = 3,

    // Food - Weight
    Kg = 10,
    G = 11,
    Lb = 12,
    Oz = 13,

    // Food - Volume
    Liter = 20,
    Ml = 21,

    // Food - Serving
    Portion = 30,
    Slice = 31,
    Serving = 32,

    // Retail - Length
    Meter = 40,
    Roll = 41,

    // Service
    Hour = 50,
    Session = 51,
    Service = 52
}
