## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/entities:
    put:
      operationId: publish-entity
      summary: Publish entity
      description: >-
        Publish an entity for ingest into the Entities API. Entities created
        with this method are "owned" by the originator: other sources, 

        such as the UI, may not edit or delete these entities. The server
        validates entities at API call time and 

        returns an error if the entity is invalid.


        An entity ID must be provided when calling this endpoint. If the entity
        referenced by the entity ID does not exist

        then it will be created. Otherwise the entity will be updated. An entity
        will only be updated if its

        provenance.sourceUpdateTime is greater than the
        provenance.sourceUpdateTime of the existing entity.
      tags:
        - subpackage_entities
      parameters:
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '200':
          description: The request was valid and accepted.
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
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/Entity'
servers:
  - url: https://example.developer.anduril.com
