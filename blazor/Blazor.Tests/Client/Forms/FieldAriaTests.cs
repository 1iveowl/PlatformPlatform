using Account.Client;
using Blazor.Client.Forms;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Forms;

namespace Blazor.Tests.Client.Forms;

// The shared mapping from an EditContext's messages to the attributes a field renders. Both message sources are covered:
// a data annotations store and the mapper's own store, which is where an account API field error lands.
public sealed class FieldAriaTests
{
    private readonly EditContext _editContext = new(new ContactForm());

    private ContactForm Model => (ContactForm)_editContext.Model;

    [Fact]
    public void For_WhenTheFieldHasNoMessage_ShouldDescribeTheFieldWithoutMarkingItInvalid()
    {
        // Arrange
        var fieldAria = new FieldAria(_editContext, "contact-error");

        // Act
        var attributes = fieldAria.For(() => Model.Email, "email");

        // Assert
        attributes["aria-describedby"].Should().Be("email-validation contact-error");
        attributes.Should().NotContainKey("aria-invalid");
    }

    [Fact]
    public void For_WhenTheAccountApiRefusedTheField_ShouldMarkItInvalidAndKeepTheDescription()
    {
        // Arrange
        var fieldAria = new FieldAria(_editContext, "contact-error");
        var mapper = new FormErrorMapper(_editContext);
        mapper.Apply(new ApiCallProblem(400, "Bad request", null, new Dictionary<string, string[]> { ["email"] = ["Email is already in use."] }, null));

        // Act
        var attributes = fieldAria.For(() => Model.Email, "email");

        // Assert
        attributes["aria-invalid"].Should().Be("true");
        attributes["aria-describedby"].Should().Be("email-validation contact-error");
        fieldAria.IsInvalid(() => Model.Name).Should().BeFalse();
    }

    [Fact]
    public void For_WhenADataAnnotationRefusedTheField_ShouldMarkItInvalid()
    {
        // Arrange
        var fieldAria = new FieldAria(_editContext, "contact-error");
        var store = new ValidationMessageStore(_editContext);
        store.Add(_editContext.Field(nameof(ContactForm.Name)), "Name is required.");

        // Act
        var attributes = fieldAria.For(() => Model.Name, "name");

        // Assert
        attributes["aria-invalid"].Should().Be("true");
        attributes["aria-describedby"].Should().Be("name-validation contact-error");
    }

    [Fact]
    public void For_WhenTheFormHasNoErrorAlert_ShouldDescribeTheFieldWithItsMessagesAlone()
    {
        // Arrange
        var fieldAria = new FieldAria(_editContext);

        // Act
        var attributes = fieldAria.For(() => Model.Name, "name");

        // Assert
        attributes["aria-describedby"].Should().Be("name-validation");
    }

    [Theory]
    [InlineData("email", "email-validation")]
    [InlineData("invite-email", "invite-email-validation")]
    public void ValidationId_WhenGivenAFieldName_ShouldDeriveTheIdFieldValidationRenders(string fieldName, string expected)
    {
        // Act
        var validationId = FieldAria.ValidationId(fieldName);

        // Assert
        validationId.Should().Be(expected);
    }

    [Fact]
    public void DescribedBy_WhenSomeIdsAreMissing_ShouldJoinOnlyTheOnesThatExist()
    {
        // Act and assert
        FieldAria.DescribedBy("first", null, "second").Should().Be("first second");
        FieldAria.DescribedBy(null, "  ").Should().BeNull();
    }

    private sealed class ContactForm
    {
        public string Name { get; } = "";

        public string Email { get; } = "";
    }
}
