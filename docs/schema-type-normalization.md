# Schema type normalization

TurboBoard preserves the SQL Server provider type for display while mapping known types into provider-neutral categories owned by `TurboBoard.Core`. Capabilities are declared from the normalized category rather than inferred in the browser.

| Normalized category | SQL Server provider types |
| --- | --- |
| Boolean | `bit` |
| Integer | `tinyint`, `smallint`, `int`, `bigint` |
| Decimal | `decimal`, `numeric`, `money`, `smallmoney` |
| FloatingPoint | `float`, `real` |
| Text | `char`, `nchar`, `varchar`, `nvarchar`, `text`, `ntext`, `xml` |
| Date | `date` |
| DateTime | `datetime`, `datetime2`, `smalldatetime`, `datetimeoffset` |
| Time | `time` |
| Guid | `uniqueidentifier` |
| Binary | `binary`, `varbinary`, `image`, `rowversion`, `timestamp` |
| Unknown | Any provider type not listed above |

Known categories support selection, filtering, and sorting. All known categories except Binary support grouping. Integer, Decimal, and FloatingPoint additionally support aggregation.

Unknown types remain visible with their original provider type and column metadata, but expose no query capabilities. This preserves Schema fidelity without treating an unfamiliar provider type as safe for query construction.
