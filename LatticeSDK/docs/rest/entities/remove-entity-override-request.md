## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/entities/{entityId}/override/{fieldPath}:
    delete:
      operationId: remove-entity-override
      summary: Delete entity override
      description: >-
        This operation clears the override value from the specified field path
        on the entity.
      tags:
        - subpackage_entities
      parameters:
        - name: entityId
          in: path
          description: The unique ID of the entity to undo an override from.
          required: true
          schema:
            type: string
        - name: fieldPath
          in: path
          description: The fieldPath to clear overrides from.
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
          description: The removal of entity override was successful.
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
servers:
  - url: https://example.developer.anduril.com
