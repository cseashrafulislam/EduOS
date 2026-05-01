using EduOS.Core.DTOs.Academic;
using FluentValidation;
using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Service.Validators
{
    public class ClassCreateValidator : AbstractValidator<ClassCreateDto>
    {
        public ClassCreateValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Class name is required")
                .MaximumLength(100).WithMessage("Class name max 100 characters");

            RuleFor(x => x.NumericValue)
                .GreaterThan(0).WithMessage("Numeric value must be greater than 0");
        }
    }

    public class ClassUpdateValidator : AbstractValidator<ClassUpdateDto>
    {
        public ClassUpdateValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Class name is required")
                .MaximumLength(100);

            RuleFor(x => x.NumericValue)
                .GreaterThan(0);
        }
    }
}

