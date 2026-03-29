# Override entity

PUT https://example.developer.anduril.com/api/v1/entities/{entityId}/override/{fieldPath}
Content-Type: application/json

Only fields marked with overridable can be overridden. Please refer to our documentation to see the comprehensive
list of fields that can be overridden. The entity in the request body should only have a value set on the field 
specified in the field path parameter. Field paths are rooted in the base entity object and must be represented 
using lower_snake_case. Do not include "entity" in the field path.

Note that overrides are applied in an eventually consistent manner. If multiple overrides are created 
concurrently for the same field path, the last writer wins.

Reference: https://developer.anduril.com/reference/rest/entities/override-entity

