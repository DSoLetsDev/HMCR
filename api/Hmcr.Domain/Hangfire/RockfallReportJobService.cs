﻿using CsvHelper;
using Hmcr.Data.Database;
using Hmcr.Data.Repositories;
using Hmcr.Domain.CsvHelpers;
using Hmcr.Domain.Hangfire.Base;
using Hmcr.Domain.Services;
using Hmcr.Model;
using Hmcr.Model.Dtos.RockfallReport;
using Hmcr.Model.Dtos.SubmissionObject;
using Hmcr.Model.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hmcr.Domain.Hangfire
{
    public interface IRockfallReportJobService
    {
        Task ProcessSubmission(SubmissionDto submission);
    }

    public class RockfallReportJobService : ReportJobServiceBase, IRockfallReportJobService
    {
        
        private IFieldValidatorService _validator;
        private IRockfallReportRepository _rockfallReportRepo;
        private ILogger<IRockfallReportJobService> _logger;

        public RockfallReportJobService(IUnitOfWork unitOfWork, ILogger<IRockfallReportJobService> logger, 
            ISubmissionStatusRepository statusRepo, ISubmissionObjectRepository submissionRepo,
            ISumbissionRowRepository submissionRowRepo, IRockfallReportRepository rockfallReportRepo, IFieldValidatorService validator)
            : base(unitOfWork, statusRepo, submissionRepo, submissionRowRepo)
        {
            _logger = logger;
            _rockfallReportRepo = rockfallReportRepo;
            _validator = validator;
        }

        public async Task ProcessSubmission(SubmissionDto submissionDto)
        {
            _logger.LogInformation("[Hangfire] Starting submission {submissionObjectId}", submissionDto.SubmissionObjectId);
            var errors = new Dictionary<string, List<string>>();

            await SetStatusesAsync();
            await SetSubmissionAsync(submissionDto);

            var (untypedRows, headers) = ParseRowsUnTyped(errors);

            if (!CheckCommonMandatoryHeaders(untypedRows, new RockfallReportHeaders(), errors))
            {
                if (errors.Count > 0)
                {
                    _submission.ErrorDetail = errors.GetErrorDetail();
                    _submission.SubmissionStatusId = _errorFileStatusId;
                    await _unitOfWork.CommitAsync();
                    return;
                }
            }

            //text after duplicate lines are removed. Will be used for importing to typed DTO.
            var text = await SetRowIdAndRemoveDuplicate(untypedRows, headers);

            foreach (var untypedRow in untypedRows)
            {
                errors = new Dictionary<string, List<string>>();
                var submissionRow = await _submissionRowRepo.GetSubmissionRowByRowId(untypedRow.RowId);
                submissionRow.RowStatusId = _successRowStatusId; //set the initial row status as success 

                var entityName = GetValidationEntityName(untypedRow);

                _validator.Validate(entityName, untypedRow, errors);

                if (errors.Count > 0)
                {
                    SetErrorDetail(submissionRow, errors);
                }
            }

            var typedRows = new List<RockfallReportDto>();

            if (_submission.SubmissionStatusId != _errorFileStatusId)
            {
                typedRows = ParseRowsTyped(text, errors);
                await PerformAdditionalValidationAsync(typedRows);
            }

            if (_submission.SubmissionStatusId == _errorFileStatusId)
            {
                await _unitOfWork.CommitAsync();
            }
            else

            {
                _submission.SubmissionStatusId = _successFileStatusId;

                await foreach (var entity in _rockfallReportRepo.SaveRockfallReportAsnyc(_submission, typedRows)) { }

                await _unitOfWork.CommitAsync();

                _logger.LogInformation($"[Hangfire] Submission {_submission.SubmissionObjectId} processed successfully.");
            }

            _logger.LogInformation("[Hangfire] Finishing submission {submissionObjectId}", _submission.SubmissionObjectId);
        }

        private async Task PerformAdditionalValidationAsync(List<RockfallReportDto> typedRows)
        {
            foreach (var typedRow in typedRows)
            {
                var errors = new Dictionary<string, List<string>>();
                var submissionRow = await _submissionRowRepo.GetSubmissionRowByRowNum(_submission.SubmissionObjectId, (decimal)typedRow.RowNum);

                if (typedRow.StartOffset != null && typedRow.EndOffset < typedRow.StartOffset)
                {
                    errors.AddItem("StartOffset", "Start Offset cannot be greater than End Offset");
                }

                //Geo-spatial Validation here

                if (errors.Count > 0)
                {
                    SetErrorDetail(submissionRow, errors);
                }
            }
        }

        private string GetValidationEntityName(RockfallReportCsvDto untypedRow)
        {
            return untypedRow.StartLatitude.IsEmpty() ? Entities.RockfallReportLrs : Entities.RockfallReportGps;
        }

        private (List<RockfallReportCsvDto> untypedRows, string headers) ParseRowsUnTyped(Dictionary<string, List<string>> errors)
        {
            var text = Encoding.UTF8.GetString(_submission.DigitalRepresentation);

            using var stringReader = new StringReader(text);
            using var csv = new CsvReader(stringReader);

            CsvHelperUtils.Config(errors, csv, false);
            csv.Configuration.RegisterClassMap<RockfallReportCsvDtoMap>();

            var rows = csv.GetRecords<RockfallReportCsvDto>().ToList();
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].RowNum = i + 1;
            }

            return (rows, GetHeader(text));
        }

        private List<RockfallReportDto> ParseRowsTyped(string text, Dictionary<string, List<string>> errors)
        {
            using var stringReader = new StringReader(text);
            using var csv = new CsvReader(stringReader);

            CsvHelperUtils.Config(errors, csv, false);
            csv.Configuration.RegisterClassMap<RockfallReportDtoMap>();

            var rows = csv.GetRecords<RockfallReportDto>().ToList();
            return rows;
        }
    }
}
