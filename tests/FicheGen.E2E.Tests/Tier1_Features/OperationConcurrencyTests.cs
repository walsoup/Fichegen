using System;
using FicheGen.App.ViewModels;
using FluentAssertions;
using Xunit;

namespace FicheGen.E2E.Tests.Tier1_Features;

public sealed class OperationConcurrencyTests
{
    [Fact]
    public void EndOperation_WhenOlderOperationCompletes_DoesNotClearBusyStateOfNewerOperation()
    {
        var vm = new ResultViewModel();

        // 1. Operation A starts
        var opA = vm.BeginOperation("fiche");
        vm.IsBusy.Should().BeTrue();
        vm.ActiveGenerator.Should().Be("fiche");

        // 2. Operation B starts while A is in flight
        var opB = vm.BeginOperation("evaluation");
        vm.IsBusy.Should().BeTrue();
        vm.ActiveGenerator.Should().Be("evaluation");

        // 3. Operation A completes (finally block of A)
        vm.EndOperation(opA);

        // 4. Busy state must remain true because Operation B is still active!
        vm.IsBusy.Should().BeTrue("older operation A must not clear IsBusy while newer operation B is running");
        vm.ActiveGenerator.Should().Be("evaluation");

        // 5. Operation B completes
        vm.EndOperation(opB);
        vm.IsBusy.Should().BeFalse();
        vm.ActiveGenerator.Should().BeNull();
    }
}
