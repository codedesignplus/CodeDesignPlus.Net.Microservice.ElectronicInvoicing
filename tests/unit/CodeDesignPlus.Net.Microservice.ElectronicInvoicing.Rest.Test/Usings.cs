global using Mapster;
global using MapsterMapper;
global using MediatR;
global using Moq;
global using System;
global using System.Threading;
global using System.Threading.Tasks;
global using Xunit;
global using Microsoft.AspNetCore.Mvc;
global using CodeDesignPlus.Net.Serializers;
global using NodaTime;

global using CodeDesignPlus.Microservice.Api.Dtos;
global using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Rest.Controllers;
global using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Commands.CreateElectronicDocument;
global using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.DataTransferObjects;
global using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetElectronicDocumentById;
