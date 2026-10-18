using FoodParkDigital.Domain.Exceptions;
using FoodParkDigital.Domain.Reviews.Entities;
using FoodParkDigital.Domain.Reviews.Enums;

namespace FoodParkDigital.Domain.Tests;

/// <summary>
/// What a customer says about a business (Fase 24H, ADR 0030): stars and a comment its author edits, the business's
/// answer, a report that never takes it down, and the platform that hides it, keeps it or restores it.
/// </summary>
public class BusinessReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.FromHours(-6));

    [Fact]
    public void Write_ShouldKeepTheStars_TheCommentTrimmed_AndWhoWroteIt()
    {
        var review = Review(comment: "  Los chilaquiles, muy ricos.  ");

        Assert.Equal((4, "Los chilaquiles, muy ricos.", "Mariana O.", Now), (review.Rating, review.Comment, review.AuthorName, review.WrittenAt));
        Assert.True(review.IsPublished);
        Assert.Null(review.EditedAt);
        // Stars alone are a review too.
        Assert.Null(Review(comment: "   ").Comment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Write_ShouldRefuse_StarsOutsideOneToFive(int rating)
    {
        var error = Assert.Throws<DomainException>(() => Review(rating: rating));
        Assert.Equal(BusinessReview.RatingMessage, error.Message);
    }

    [Fact]
    public void Write_ShouldRefuse_ACommentLongerThanFiveHundred()
    {
        Assert.Equal(500, Review(comment: new string('a', 500)).Comment!.Length);
        var error = Assert.Throws<DomainException>(() => Review(comment: new string('a', 501)));
        Assert.Equal(BusinessReview.CommentTooLongMessage, error.Message);
    }

    [Theory]
    [InlineData("Pidan en https://otra-tienda.mx")]
    [InlineData("mejor en www.otra.com")]
    [InlineData("Visiten tacosdonpepe.com")]
    [InlineData("Escríbanme a mariana@correo.com")]
    [InlineData("Llámenme al 55 1234 5678")]
    [InlineData("whats (222) 123-45-67")]
    public void Write_ShouldRefuse_LinksEmailsAndPhones(string comment)
    {
        var error = Assert.Throws<DomainException>(() => Review(comment: comment));
        Assert.Equal(BusinessReview.CommentContactMessage, error.Message);
    }

    [Theory]
    // A missing space after a period is not a link, nor a price, a date or an order with numbers.
    [InlineData("Muy rico.Me encantó, volveré.")]
    [InlineData("Pedí 2 tacos y 3 quesadillas por $185.50 el 18/10/2026")]
    [InlineData("10 de 10, el pedido 1234 llegó a tiempo")]
    public void Write_ShouldKeep_WhatOnlyLooksLikeAContact(string comment)
    {
        Assert.Equal(comment, Review(comment: comment).Comment);
    }

    [Theory]
    [InlineData("Mariana", "Ortiz", "Mariana O.")]
    [InlineData("  María Fernanda ", " lópez", "María Fernanda L.")]
    [InlineData("Beto", null, "Beto")]
    [InlineData(null, "Ortiz", "Cliente")]
    [InlineData("   ", "   ", "Cliente")]
    public void AuthorName_ShouldBeTheFirstNameAndTheInitialOfTheLast(string? first, string? last, string expected)
    {
        Assert.Equal(expected, BusinessReview.AuthorNameFrom(first, last));
        Assert.True(BusinessReview.AuthorNameFrom(new string('a', 80), "Ortiz").Length <= BusinessReview.MaxAuthorNameLength);
    }

    [Fact]
    public void Edit_ShouldChangeItAndSayWhen_ButNotOnceThePlatformHidIt()
    {
        var review = Review();

        review.Edit("Mariana O.", 2, "Llegó frío.", Now.AddDays(1));
        Assert.Equal((2, "Llegó frío.", Now, Now.AddDays(1)), (review.Rating, review.Comment, review.WrittenAt, review.EditedAt));

        review.Hide(ReviewConcern.Offensive, Now.AddDays(2));
        var error = Assert.Throws<DomainException>(() => review.Edit("Mariana O.", 5, null, Now.AddDays(3)));
        Assert.Equal(BusinessReview.HiddenForAuthorMessage, error.Message);
    }

    [Fact]
    public void Reply_ShouldBeOneAnswer_TheBusinessChangesOrRemoves_WithTheSameRules()
    {
        var review = Review();

        review.ReplyWith("  ¡Gracias por venir!  ", Now);
        review.ReplyWith("Gracias, te esperamos.", Now.AddHours(1));
        Assert.Equal(("Gracias, te esperamos.", Now.AddHours(1)), (review.Reply, review.RepliedAt));

        Assert.Equal(BusinessReview.ReplyRequiredMessage, Assert.Throws<DomainException>(() => review.ReplyWith("  ", Now)).Message);
        Assert.Equal(BusinessReview.ReplyTooLongMessage, Assert.Throws<DomainException>(() => review.ReplyWith(new string('a', 501), Now)).Message);
        Assert.Equal(BusinessReview.ReplyContactMessage, Assert.Throws<DomainException>(() => review.ReplyWith("Escríbenos a hola@tienda.mx", Now)).Message);

        review.RemoveReply();
        Assert.Equal((null, (DateTimeOffset?)null), (review.Reply, review.RepliedAt));
    }

    [Fact]
    public void Report_ShouldLeaveItPublished_UntilThePlatformDecides()
    {
        var review = Review();

        review.Report(ReviewConcern.NotAnExperience, Now);

        // A business never takes down what a customer said.
        Assert.True(review.IsPublished);
        Assert.Equal((Now, ReviewConcern.NotAnExperience), (review.ReportedAt, review.ReportReason));
        Assert.Equal(BusinessReview.AlreadyReportedMessage,
            Assert.Throws<DomainException>(() => review.Report(ReviewConcern.Spam, Now)).Message);
        Assert.Equal(BusinessReview.ConcernMessage,
            Assert.Throws<DomainException>(() => Review().Report((ReviewConcern)9, Now)).Message);
    }

    [Fact]
    public void Keep_ShouldCloseTheReport_AndOnlyAChangeByItsAuthorLetsTheBusinessReportItAgain()
    {
        var review = Review();
        review.Report(ReviewConcern.Offensive, Now);

        review.Keep(Now.AddHours(1));
        Assert.Equal(((DateTimeOffset?)null, (ReviewConcern?)null, Now.AddHours(1)), (review.ReportedAt, review.ReportReason, review.ModeratedAt));
        Assert.Equal(BusinessReview.AlreadyKeptMessage,
            Assert.Throws<DomainException>(() => review.Report(ReviewConcern.Offensive, Now.AddHours(2))).Message);
        Assert.Equal(BusinessReview.NotReportedMessage, Assert.Throws<DomainException>(() => review.Keep(Now)).Message);

        review.Edit("Mariana O.", 1, "Otra cosa.", Now.AddHours(3));
        review.Report(ReviewConcern.Offensive, Now.AddHours(4));
        Assert.Equal(Now.AddHours(4), review.ReportedAt);
    }

    [Fact]
    public void Hide_ShouldTakeItDownWithItsReason_ClosingAnyReport_AndRestorePutsItBack()
    {
        var review = Review();
        review.Report(ReviewConcern.PersonalData, Now);

        review.Hide(ReviewConcern.PersonalData, Now.AddHours(1));
        Assert.Equal((ReviewStatus.Hidden, ReviewConcern.PersonalData, (DateTimeOffset?)null, Now.AddHours(1)),
            (review.Status, review.HiddenReason, review.ReportedAt, review.ModeratedAt));
        Assert.Equal(BusinessReview.HiddenMessage, Assert.Throws<DomainException>(() => review.ReplyWith("Gracias", Now)).Message);
        Assert.Equal(BusinessReview.HiddenMessage, Assert.Throws<DomainException>(() => review.Report(ReviewConcern.Spam, Now)).Message);
        Assert.Throws<DomainException>(() => review.Hide(ReviewConcern.Spam, Now));

        review.Restore(Now.AddHours(2));
        Assert.Equal((ReviewStatus.Published, (ReviewConcern?)null, Now.AddHours(2)), (review.Status, review.HiddenReason, review.ModeratedAt));
        Assert.Equal(BusinessReview.NotHiddenMessage, Assert.Throws<DomainException>(() => review.Restore(Now)).Message);
    }

    private static BusinessReview Review(int rating = 4, string? comment = "Muy rico.") =>
        BusinessReview.Write(Guid.NewGuid(), Guid.NewGuid(), "Mariana O.", rating, comment, Now);
}
