## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/entities/{entityId}:
    get:
      operationId: get-entity
      summary: Get entity
      tags:
        - subpackage_entities
      parameters:
        - name: entityId
          in: path
          description: ID of the entity to return
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
          description: Entity retrieval was successful
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
