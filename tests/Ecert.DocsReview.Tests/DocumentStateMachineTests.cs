using Ecert.DocsReview.Api.Domain;

namespace Ecert.DocsReview.Tests;

public class DocumentStateMachineTests
{
    public static readonly TheoryData<DocumentStatus, DocumentStatus> ValidTransitions = new()
    {
        { DocumentStatus.Created, DocumentStatus.PendingReview },
        { DocumentStatus.PendingReview, DocumentStatus.UnderReview },
        { DocumentStatus.UnderReview, DocumentStatus.Approved },
        { DocumentStatus.UnderReview, DocumentStatus.Rejected },
        { DocumentStatus.Rejected, DocumentStatus.PendingReview },
        { DocumentStatus.Approved, DocumentStatus.Archived },
        { DocumentStatus.Rejected, DocumentStatus.Archived },
    };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void CanTransition_AllowsValidTransitions(DocumentStatus from, DocumentStatus to)
    {
        Assert.True(DocumentStateMachine.CanTransition(from, to));
    }

    [Fact]
    public void CanTransition_RejectsEveryPairNotInTheValidSet()
    {
        var allStatuses = Enum.GetValues<DocumentStatus>();
        var valid = ValidTransitions
            .Select(row => ((DocumentStatus)row[0], (DocumentStatus)row[1]))
            .ToHashSet();

        foreach (var from in allStatuses)
        {
            foreach (var to in allStatuses)
            {
                if (!valid.Contains((from, to)))
                {
                    Assert.False(
                        DocumentStateMachine.CanTransition(from, to),
                        $"Transition {from} -> {to} should be invalid.");
                }
            }
        }
    }

    [Fact]
    public void CanTransition_ArchivedIsTerminal()
    {
        foreach (var to in Enum.GetValues<DocumentStatus>())
        {
            Assert.False(DocumentStateMachine.CanTransition(DocumentStatus.Archived, to));
        }
    }

    [Theory]
    [InlineData(DocumentStatus.Created, true)]
    [InlineData(DocumentStatus.PendingReview, true)]
    [InlineData(DocumentStatus.Rejected, true)]
    [InlineData(DocumentStatus.UnderReview, false)]
    [InlineData(DocumentStatus.Approved, false)]
    [InlineData(DocumentStatus.Archived, false)]
    public void CanUploadVersion_OnlyAllowedInEditableStates(DocumentStatus status, bool expected)
    {
        Assert.Equal(expected, DocumentStateMachine.CanUploadVersion(status));
    }

    [Fact]
    public void StatusAfterVersionUpload_RejectedGoesBackToPendingReview()
    {
        Assert.Equal(
            DocumentStatus.PendingReview,
            DocumentStateMachine.StatusAfterVersionUpload(DocumentStatus.Rejected));
    }

    [Theory]
    [InlineData(DocumentStatus.Created)]
    [InlineData(DocumentStatus.PendingReview)]
    public void StatusAfterVersionUpload_KeepsStatusWhenAlreadyEditable(DocumentStatus status)
    {
        Assert.Equal(status, DocumentStateMachine.StatusAfterVersionUpload(status));
    }

    [Theory]
    [InlineData(DocumentStatus.UnderReview)]
    [InlineData(DocumentStatus.Approved)]
    [InlineData(DocumentStatus.Archived)]
    public void StatusAfterVersionUpload_ThrowsWhenUploadNotAllowed(DocumentStatus status)
    {
        Assert.Throws<InvalidOperationException>(
            () => DocumentStateMachine.StatusAfterVersionUpload(status));
    }
}
