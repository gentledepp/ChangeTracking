﻿using FluentAssertions;
using Xunit;

namespace ChangeTracking.Tests
{
    public class IEditableObjectTests
    {
        [Fact]
        public void AsTrackable_Should_Make_Object_Implement_IEditableObject()
        {
            var order = Helper.GetOrder();

            Order trackable = order.AsTrackable();

            trackable.Should().BeAssignableTo<System.ComponentModel.IEditableObject>();
        }

        [Fact]
        public void CancelEdit_On_Item_Should_Revert_Changes()
        {
            var order = Helper.GetOrder();

            var trackable = order.AsTrackable();
            var editableObject = (System.ComponentModel.IEditableObject)trackable;

            editableObject.BeginEdit();
            trackable.CustomerNumber = "Testing";
            editableObject.CancelEdit();

            trackable.CustomerNumber.Should().Be("Test", because: "item was canceled");
        }

        [Fact]
        public void CancelEdit_On_Item_After_EndEdit_Should_Not_Revert_Changes()
        {
            var order = Helper.GetOrder();

            var trackable = order.AsTrackable();
            var editableObject = (System.ComponentModel.IEditableObject)trackable;

            editableObject.BeginEdit();
            trackable.CustomerNumber = "Testing";
            editableObject.EndEdit();
            editableObject.CancelEdit();

            trackable.CustomerNumber.Should().Be("Testing", because: "item was canceled after calling EndEdit");
        }

        [Fact]
        public void With_Out_BeginEdit_CancelEdit_Should_Do_Nothing()
        {
            var order = Helper.GetOrder();

            var trackable = order.AsTrackable();
            var editableObject = (System.ComponentModel.IEditableObject)trackable;

            trackable.CustomerNumber = "Testing";
            editableObject.CancelEdit();

            trackable.CustomerNumber.Should().Be("Testing", because: "item was canceled after calling EndEdit");
        }
        
        [Fact]
        public void AsTrackable_Should_Make_Object_Complex_Property_Implement_IEditableObject()
        {
            var order = Helper.GetOrder();

            Order trackable = order.AsTrackable();

            trackable.Address.Should().BeAssignableTo<System.ComponentModel.IEditableObject>();
        }

        [Fact]
        public void AsTrackable_Should_Not_Make_Object_Complex_Property_Implement_IEditableObject_If_Passed_False()
        {
            var order = Helper.GetOrder();

            Order trackable = order.AsTrackable(makeComplexPropertiesTrackable: false);

            (trackable.Address as System.ComponentModel.IEditableObject).Should().BeNull();
        }

        [Fact]
        public void CancelEdit_On_Item_Should_Revert_Changes_On_Complex_Property()
        {
            var order = Helper.GetOrder();

            var trackable = order.AsTrackable();
            var editableObject = (System.ComponentModel.IEditableObject)trackable;

            editableObject.BeginEdit();
            trackable.Address.City = "Chicago";
            editableObject.CancelEdit();

            trackable.Address.City.Should().Be("New York", because: "parent item was canceled");
        }
    }
}
