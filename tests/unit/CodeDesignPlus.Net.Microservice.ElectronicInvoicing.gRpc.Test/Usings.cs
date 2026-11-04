global using Mapster;
global using MapsterMapper;
global using MediatR;
global using Moq;
global using System;
global using System.Threading;
global using System.Threading.Tasks;
global using Xunit;
global using Grpc.Core;
global using Grpc.Core.Testing;
global using CodeDesignPlus.Net.Exceptions;
global using CodeDesignPlus.Net.Serializers;

global using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc;
global using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.gRpc.Services;
global using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.DataTransferObjects;
global using CodeDesignPlus.Net.Microservice.ElectronicInvoicing.Application.ElectronicDocument.Queries.GetElectronicDocumentById;
