## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/entities/events:
    post:
      operationId: long-poll-entity-events
      summary: Poll entity events
      description: >-
        This is a long polling API that will first return all pre-existing data
        and then return all new data as

        it becomes available. If you want to start a new polling session then
        open a request with an empty

        'sessionToken' in the request body. The server will return a new session
        token in the response.

        If you want to retrieve the next batch of results from an existing
        polling session then send the session

        token you received from the server in the request body. If no new data
        is available then the server will

        hold the connection open for up to 5 minutes. After the 5 minute timeout
        period, the server will close the 

        connection with no results and you may resume polling with the same
        session token. If your session falls behind 

        more than 3x the total number of entities in the environment, the server
        will terminate your session. 

        In this case you must start a new session by sending a request with an
        empty session token.
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
          description: Entity event batch retrieval was successful
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/EntityEventResponse'
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
        '408':
          description: >-
            The server has terminated the session. The server will send this
            error when the client has fallen too far

            behind in processing entity events. If the server sends this error,
            then the session token is invalid and a

            new session must be initiated to receive entity events.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
        '429':
          description: >-
            Server is out of resources or reaching rate limiting or quota and
            cannot accept the request at this time.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Error'
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/EntityEventRequest'
servers:
  - url: https://example.developer.anduril.com
