using FoodParkDigital.Domain.Branches.Entities;
using FoodParkDigital.Domain.Branches.Enums;
using FoodParkDigital.Domain.Common.ValueObjects;
using FoodParkDigital.Domain.Exceptions;

namespace FoodParkDigital.Domain.Tests;

/// <summary>Where a branch is on the map (Fase 24G, ADR 0029), and the distance Explorar measures to it.</summary>
public class BranchLocationTests
{
    // The Zócalo of Puebla and of Mexico City: about 107 km in a straight line.
    private static readonly GeoPoint Puebla = new(19.043577m, -98.198626m);
    private static readonly GeoPoint Cdmx = new(19.432608m, -99.133209m);

    [Fact]
    public void GeoPoint_ShouldKeepSixDecimals_LatitudeFirst()
    {
        var point = new GeoPoint(19.04357749m, -98.19862651m);

        Assert.Equal((19.043577m, -98.198627m), (point.Latitude, point.Longitude));
        Assert.Equal(new GeoPoint(19.043577m, -98.198627m), point);
    }

    [Theory]
    [InlineData(90.000001, 0)]
    [InlineData(-90.5, 10)]
    [InlineData(19.04, 180.01)]
    [InlineData(19.04, -181)]
    public void GeoPoint_ShouldRefuse_APointOffTheMap(decimal latitude, decimal longitude)
    {
        var error = Assert.Throws<DomainException>(() => new GeoPoint(latitude, longitude));
        Assert.Equal(GeoPoint.InvalidMessage, error.Message);
    }

    [Fact]
    public void GeoPoint_ShouldRefuse_ZeroZero_WhatADeviceWithoutAPositionWrites()
    {
        var error = Assert.Throws<DomainException>(() => new GeoPoint(0m, 0m));
        Assert.Equal(GeoPoint.NullIslandMessage, error.Message);
        // Only both: the equator and the meridian of Greenwich have places.
        Assert.Equal(0m, new GeoPoint(0m, -78.45m).Latitude);
    }

    [Fact]
    public void KilometersTo_ShouldMeasureAStraightLineOverTheEarth()
    {
        Assert.InRange(Puebla.KilometersTo(Cdmx), 106.5, 108);
        Assert.Equal(Puebla.KilometersTo(Cdmx), Cdmx.KilometersTo(Puebla), 9);
        Assert.Equal(0, Puebla.KilometersTo(Puebla), 9);
        // A block away: about 111 m per thousandth of a degree of latitude.
        Assert.InRange(Puebla.KilometersTo(new GeoPoint(19.044577m, -98.198626m)), 0.105, 0.116);
    }

    [Theory]
    // Rounded to six decimals, as it would be kept: 0, 0.
    [InlineData(0.0000001, 0.0000002)]
    [InlineData(-0.0000004, 0.0000004)]
    public void GeoPoint_ShouldCheckWhatItKeeps_SoATinyPointIsTheZeroZeroItBecomes(decimal latitude, decimal longitude)
    {
        var error = Assert.Throws<DomainException>(() => new GeoPoint(latitude, longitude));
        Assert.Equal(GeoPoint.NullIslandMessage, error.Message);
        Assert.Equal(GeoPoint.NullIslandMessage, GeoPoint.Problem(latitude, longitude));
    }

    [Fact]
    public void GeoPoint_ShouldTakeAPointThatRoundsIntoRange()
    {
        // 90.0000004 is kept as 90: the pole.
        Assert.Equal(90m, new GeoPoint(90.0000004m, -180.0000004m).Latitude);
        Assert.Null(GeoPoint.Problem(90.0000004m, -180.0000004m));
        Assert.Equal(GeoPoint.InvalidMessage, GeoPoint.Problem(90.0000006m, 0m));
    }

    [Fact]
    public void FromStored_ShouldLeaveABadRowOffTheMap_InsteadOfBreakingWhatReadsIt()
    {
        Assert.Equal(Puebla, GeoPoint.FromStored(19.043577m, -98.198626m));
        Assert.Null(GeoPoint.FromStored(null, null));
        Assert.Null(GeoPoint.FromStored(19.043577m, null));
        Assert.Null(GeoPoint.FromStored(0m, 0m));
        Assert.Null(GeoPoint.FromStored(91m, -98.198626m));
    }

    [Fact]
    public void Branch_ShouldBeOffTheMap_UntilItsOwnerPutsIt_AndCanBeTakenOff()
    {
        var branch = new Branch(Guid.NewGuid(), "Centro", "CEN");
        Assert.Null(branch.Location);

        branch.SetLocation(Puebla);
        Assert.Equal((Puebla, 19.043577m, -98.198626m), (branch.Location, branch.Latitude, branch.Longitude));

        branch.SetLocation(null);
        Assert.Equal((null, null, (GeoPoint?)null), (branch.Latitude, branch.Longitude, branch.Location));
    }

    [Fact]
    public void PublicLocation_ShouldBeThePointOfABranchCustomersComeTo_NeverOfOneThatOnlyDelivers()
    {
        var branch = new Branch(Guid.NewGuid(), "Centro", "CEN");
        branch.SetLocation(Puebla);
        Assert.Equal(Puebla, branch.PublicLocation);

        branch.Update(branch.Name, branch.Code, BranchContact.Empty, BranchServiceMode.DeliveryOnly);
        Assert.Null(branch.PublicLocation);
        // Kept for the team: it shows again when the branch opens its premises.
        Assert.Equal(Puebla, branch.Location);
        branch.Update(branch.Name, branch.Code, BranchContact.Empty, BranchServiceMode.Premises);
        Assert.Equal(Puebla, branch.PublicLocation);
    }
}
