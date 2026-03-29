## OpenAPI Specification

```yaml
openapi: 3.1.0
info:
  title: REST
  version: 1.0.0
paths:
  /api/v1/tasks:
    post:
      operationId: create-task
      summary: Create task
      description: >-
        Creates a new Task in the system with the specified parameters.


        This method initiates a new task with a unique ID (either provided or
        auto-generated),

        sets the initial task state to STATUS_SENT, and establishes task
        ownership. The task

        can be assigned to a specific agent through the Relations field.


        Once created, a task enters the lifecycle workflow and can be tracked,
        updated, and managed

        through other Tasks API endpoints.
      tags:
        - subpackage_tasks
      parameters:
        - name: Authorization
          in: header
          description: Bearer authentication
          required: true
          schema:
            type: string
      responses:
        '201':
          description: Task creation was successful
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/Task'
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
              $ref: '#/components/schemas/TaskCreation'
servers:
  - url: https://example.developer.anduril.com
