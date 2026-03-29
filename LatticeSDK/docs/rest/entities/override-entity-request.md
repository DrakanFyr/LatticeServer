## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/entities/{entityId}/override/{fieldPath}:
    put:
      operationId: override-entity
      summary: Override entity
      description: >-
        Only fields marked with overridable can be overridden. Please refer to
        our documentation to see the comprehensive

        list of fields that can be overridden. The entity in the request body
        should only have a value set on the field 

        specified in the field path parameter. Field paths are rooted in the
        base entity object and must be represented 

        using lower_snake_case. Do not include "entity" in the field path.


        Note that overrides are applied in an eventually consistent manner. If
        multiple overrides are created 

        concurrently for the same field path, the last writer wins.
      tags:
        - subpackage_entities
      parameters:
        - name: entityId
          in: path
          description: The unique ID of the entity to override
          required: true
          schema:
            type: string
        - name: fieldPath
          in: path
          description: fieldPath to override
          required: true
          schema:
            type: string
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: The Entities API accepts the override.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Entity'
        '400':
          description: Bad request
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '401':
          description: Unauthorized to access resource
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '404':
          description: The specified resource was not found
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/EntityOverride'
servers:
  - url: https://example.developer.anduril.com
