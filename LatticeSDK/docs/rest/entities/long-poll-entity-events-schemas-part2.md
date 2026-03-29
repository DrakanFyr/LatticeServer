    Sensors:
      type: object
      properties:
        sensors:
          type: array
          items:
            $ref: '#/components/schemas/Sensor'
      description: List of sensors available for an entity.
      title: Sensors
    PayloadConfigurationEffectiveEnvironmentItems:
      type: string
      enum:
        - ENVIRONMENT_UNKNOWN
        - ENVIRONMENT_AIR
        - ENVIRONMENT_SURFACE
        - ENVIRONMENT_SUB_SURFACE
        - ENVIRONMENT_LAND
        - ENVIRONMENT_SPACE
      title: PayloadConfigurationEffectiveEnvironmentItems
    PayloadConfigurationPayloadOperationalState:
      type: string
      enum:
        - PAYLOAD_OPERATIONAL_STATE_INVALID
        - PAYLOAD_OPERATIONAL_STATE_OFF
        - PAYLOAD_OPERATIONAL_STATE_NON_OPERATIONAL
        - PAYLOAD_OPERATIONAL_STATE_DEGRADED
        - PAYLOAD_OPERATIONAL_STATE_OPERATIONAL
        - PAYLOAD_OPERATIONAL_STATE_OUT_OF_SERVICE
        - PAYLOAD_OPERATIONAL_STATE_UNKNOWN
      description: The operational state of this payload.
      title: PayloadConfigurationPayloadOperationalState
    PayloadConfiguration:
      type: object
      properties:
        capabilityId:
          type: string
          description: |-
            Identifying ID for the capability.
             This ID may be used multiple times to represent payloads that are the same capability but have different operational states
        quantity:
          type: integer
          format: uint
          description: The number of payloads currently available in the configuration.
        effectiveEnvironment:
          type: array
          items:
            $ref: '#/components/schemas/PayloadConfigurationEffectiveEnvironmentItems'
          description: The target environments the configuration is effective against.
        payloadOperationalState:
          $ref: '#/components/schemas/PayloadConfigurationPayloadOperationalState'
          description: The operational state of this payload.
        payloadDescription:
          type: string
          description: A human readable description of the payload
      title: PayloadConfiguration
    Payload:
      type: object
      properties:
        config:
          $ref: '#/components/schemas/PayloadConfiguration'
      description: Individual payload configuration.
      title: Payload
    Payloads:
      type: object
      properties:
        payloadConfigurations:
          type: array
          items:
            $ref: '#/components/schemas/Payload'
      description: List of payloads available for an entity.
      title: Payloads
    PowerSourcePowerStatus:
      type: string
      enum:
        - POWER_STATUS_INVALID
        - POWER_STATUS_UNKNOWN
        - POWER_STATUS_NOT_PRESENT
        - POWER_STATUS_OPERATING
        - POWER_STATUS_DISABLED
        - POWER_STATUS_ERROR
      description: Status of the power source.
      title: PowerSourcePowerStatus
    PowerSourcePowerType:
      type: string
      enum:
        - POWER_TYPE_INVALID
        - POWER_TYPE_UNKNOWN
        - POWER_TYPE_GAS
        - POWER_TYPE_BATTERY
      description: Used to determine the type of power source.
      title: PowerSourcePowerType
    PowerLevel:
      type: object
      properties:
        capacity:
          type: number
          format: double
          description: Total power capacity of the system.
        remaining:
          type: number
          format: double
          description: Remaining power capacity of the system.
        percentRemaining:
          type: number
          format: double
          description: Percent of power remaining.
        voltage:
          type: number
          format: double
          description: >-
            Voltage of the power source subsystem, as reported by the power
            source. If the source does not report this value
             this field will be null.
        currentAmps:
          type: number
          format: double
          description: >-
            Current in amps of the power source subsystem, as reported by the
            power source. If the source does not
             report this value this field will be null.
        runTimeToEmptyMins:
          type: number
          format: double
          description: >-
            Estimated minutes until empty. Calculated with consumption at the
            moment, as reported by the power source. If the source does not
             report this value this field will be null.
        consumptionRateLPerS:
          type: number
          format: double
          description: Fuel consumption rate in liters per second.
      description: Represents the power level of a system.
      title: PowerLevel
    PowerSource:
      type: object
      properties:
        powerStatus:
          $ref: '#/components/schemas/PowerSourcePowerStatus'
          description: Status of the power source.
        powerType:
          $ref: '#/components/schemas/PowerSourcePowerType'
          description: Used to determine the type of power source.
        powerLevel:
          $ref: '#/components/schemas/PowerLevel'
          description: >-
            Power level of the system. If absent, the power level is assumed to
            be unknown.
        messages:
          type: array
          items:
            type: string
          description: >-
            Set of human-readable messages with status of the power system.
            Typically this would be used in an error state
             to provide additional error information. This can also be used for informational messages.
        offloadable:
          type: boolean
          description: >-
            Whether the power source is offloadable. If the value is missing (as
            opposed to false) then the entity does not
             report whether the power source is offloadable.
      description: >-
        Represents the state of a single power source that is connected to this
        entity.
      title: PowerSource
    PowerState:
      type: object
      properties:
        sourceIdToState:
          type: object
          additionalProperties:
            $ref: '#/components/schemas/PowerSource'
          description: >-
            This is a map where the key is a unique id of the power source and
            the value is additional information about the
             power source.
      description: Represents the state of power sources connected to this entity.
      title: PowerState
    OverrideStatus:
      type: string
      enum:
        - OVERRIDE_STATUS_INVALID
        - OVERRIDE_STATUS_APPLIED
        - OVERRIDE_STATUS_PENDING
        - OVERRIDE_STATUS_TIMEOUT
        - OVERRIDE_STATUS_REJECTED
        - OVERRIDE_STATUS_DELETION_PENDING
      description: status of the override
      title: OverrideStatus
    OverrideType:
      type: string
      enum:
        - OVERRIDE_TYPE_INVALID
        - OVERRIDE_TYPE_LIVE
        - OVERRIDE_TYPE_POST_EXPIRY
      description: >-
        The type of the override, defined by the stage of the entity lifecycle
        that the entity was in when the override
         was requested.
      title: OverrideType
    Override:
      type: object
      properties:
        requestId:
          type: string
          description: override request id for an override request
        fieldPath:
          type: string
          description: |-
            proto field path which is the string representation of a field.
             example: correlated.primary_entity_id would be primary_entity_id in correlated component
        maskedFieldValue:
          $ref: '#/components/schemas/Entity'
          description: >-
            new field value corresponding to field path. In the shape of an
            empty entity with only the changed value.
             example: entity: { mil_view: { disposition: Disposition_DISPOSITION_HOSTILE } }
        status:
          $ref: '#/components/schemas/OverrideStatus'
          description: status of the override
        provenance:
          $ref: '#/components/schemas/Provenance'
        type:
          $ref: '#/components/schemas/OverrideType'
          description: >-
            The type of the override, defined by the stage of the entity
            lifecycle that the entity was in when the override
             was requested.
        requestTimestamp:
          type: string
          format: date-time
          description: >-
            Timestamp of the override request. The timestamp is generated by the
            Entity Manager instance that receives the request.
      description: Details about an override. Last write wins.
      title: Override
    Overrides:
      type: object
      properties:
        override:
          type: array
          items:
            $ref: '#/components/schemas/Override'
      description: Metadata about entity overrides present.
      title: Overrides
    Indicators:
      type: object
      properties:
        simulated:
          type: boolean
        exercise:
          type: boolean
        emergency:
          type: boolean
        c2:
          type: boolean
        egressable:
          type: boolean
          description: |-
            Indicates the Entity should be egressed to external sources.
             Integrations choose how the egressing happens (e.g. if an Entity needs fuzzing).
        starred:
          type: boolean
          description: >-
            A signal of arbitrary importance such that the entity should be
            globally marked for all users
      description: Indicators to describe entity to consumers.
      title: Indicators
    HighValueTargetMatch:
      type: object
      properties:
        highValueTargetListId:
          type: string
          description: >-
            The ID of the high value target list that matches the target
            description.
        highValueTargetDescriptionId:
          type: string
          description: >-
            The ID of the specific high value target description within a high
            value target list that was matched against.
             The ID is considered to be a globally unique identifier across all high value target IDs.
      title: HighValueTargetMatch
    HighValueTarget:
      type: object
      properties:
        isHighValueTarget:
          type: boolean
          description: >-
            Indicates whether the target matches any description from a high
            value target list.
        targetPriority:
          type: integer
          format: uint
          description: >-
            The priority associated with the target. If the target's description
            appears on multiple high value target lists,
             the priority will be a reflection of the highest priority of all of those list's target description.

             A lower value indicates the target is of a higher priority, with 1 being the highest possible priority. A value of
             0 indicates there is no priority associated with this target.
        targetMatches:
          type: array
          items:
            $ref: '#/components/schemas/HighValueTargetMatch'
          description: >-
            All of the high value target descriptions that the target matches
            against.
        isHighPayoffTarget:
          type: boolean
          description: >-
            Indicates whether the target is a 'High Payoff Target'. Targets can
            be one or both of high value and high payoff.
      description: Describes whether something is a high value target or not.
      title: HighValueTarget
    Threat:
      type: object
      properties:
        isThreat:
          type: boolean
          description: Indicates that the entity has been determined to be a threat.
      description: Describes whether an entity is a threat or not.
      title: Threat
    TargetPriority:
      type: object
      properties:
        highValueTarget:
          $ref: '#/components/schemas/HighValueTarget'
          description: >-
            Describes the target priority in relation to high value target
            lists.
        threat:
          $ref: '#/components/schemas/Threat'
          description: Describes whether the entity should be treated as a threat
      description: The target prioritization associated with an entity.
      title: TargetPriority
    Fixed:
      type: object
      properties: {}
      description: >-
        A fix of a signal. No extra fields but it is expected that location
        should be populated when using this report.
      title: Fixed
    EmitterNotation:
      type: object
      properties:
        emitterNotation:
          type: string
        confidence:
          type: number
          format: double
          description: >-
            confidence as a percentage that the emitter notation in this
            component is accurate
      description: A representation of a single emitter notation.
      title: EmitterNotation
    PulseRepetitionInterval:
      type: object
      properties:
        pulseRepetitionIntervalS:
          $ref: '#/components/schemas/Measurement'
      description: A component that describe the length in time between two pulses
      title: PulseRepetitionInterval
    ScanCharacteristicsScanType:
      type: string
      enum:
        - SCAN_TYPE_INVALID
        - SCAN_TYPE_CIRCULAR
        - SCAN_TYPE_BIDIRECTIONAL_HORIZONTAL_SECTOR
        - SCAN_TYPE_BIDIRECTIONAL_VERTICAL_SECTOR
        - SCAN_TYPE_NON_SCANNING
        - SCAN_TYPE_IRREGULAR
        - SCAN_TYPE_CONICAL
        - SCAN_TYPE_LOBE_SWITCHING
        - SCAN_TYPE_RASTER
        - SCAN_TYPE_CIRCULAR_VERTICAL_SECTOR
        - SCAN_TYPE_CIRCULAR_CONICAL
        - SCAN_TYPE_SECTOR_CONICAL
        - SCAN_TYPE_AGILE_BEAM
        - SCAN_TYPE_UNIDIRECTIONAL_VERTICAL_SECTOR
        - SCAN_TYPE_UNIDIRECTIONAL_HORIZONTAL_SECTOR
        - SCAN_TYPE_UNIDIRECTIONAL_SECTOR
        - SCAN_TYPE_BIDIRECTIONAL_SECTOR
      title: ScanCharacteristicsScanType
    ScanCharacteristics:
      type: object
      properties:
        scanType:
          $ref: '#/components/schemas/ScanCharacteristicsScanType'
        scanPeriodS:
          type: number
          format: double
      description: A component that describes the scanning characteristics of a signal
      title: ScanCharacteristics
    Signal:
      type: object
      properties:
        frequencyCenter:
          $ref: '#/components/schemas/Frequency'
        frequencyRange:
          $ref: '#/components/schemas/FrequencyRange'
        bandwidthHz:
          type: number
          format: double
          description: Indicates the bandwidth of a signal (Hz).
        signalToNoiseRatio:
          type: number
          format: double
          description: Indicates the signal to noise (SNR) of this signal.
        lineOfBearing:
          $ref: '#/components/schemas/LineOfBearing'
        fixed:
          $ref: '#/components/schemas/Fixed'
        emitterNotations:
          type: array
          items:
            $ref: '#/components/schemas/EmitterNotation'
          description: Emitter notations associated with this entity.
        pulseWidthS:
          type: number
          format: double
          description: length in time of a single pulse
        pulseRepetitionInterval:
          $ref: '#/components/schemas/PulseRepetitionInterval'
          description: length in time between the start of two pulses
        scanCharacteristics:
          $ref: '#/components/schemas/ScanCharacteristics'
          description: describes how a signal is observing the environment
      description: A component that describes an entity's signal characteristics.
      title: Signal
    TransponderCodesMode4InterrogationResponse:
      type: string
      enum:
        - INTERROGATION_RESPONSE_INVALID
        - INTERROGATION_RESPONSE_CORRECT
        - INTERROGATION_RESPONSE_INCORRECT
        - INTERROGATION_RESPONSE_NO_RESPONSE
      description: The validity of the response from the Mode 4 interrogation.
      title: TransponderCodesMode4InterrogationResponse
    Mode5Mode5InterrogationResponse:
      type: string
      enum:
        - INTERROGATION_RESPONSE_INVALID
        - INTERROGATION_RESPONSE_CORRECT
        - INTERROGATION_RESPONSE_INCORRECT
        - INTERROGATION_RESPONSE_NO_RESPONSE
      description: The validity of the response from the Mode 5 interrogation.
      title: Mode5Mode5InterrogationResponse
    Mode5:
      type: object
      properties:
        mode5InterrogationResponse:
          $ref: '#/components/schemas/Mode5Mode5InterrogationResponse'
          description: The validity of the response from the Mode 5 interrogation.
        mode5:
          type: integer
          format: uint
          description: The Mode 5 code assigned to military assets.
        mode5PlatformId:
          type: integer
          format: uint
          description: The Mode 5 platform identification code.
      description: Describes the Mode 5 transponder interrogation status and codes.
      title: Mode5
    ModeS:
      type: object
      properties:
        id:
          type: string
          description: Mode S identifier which comprises of 8 alphanumeric characters.
        address:
          type: integer
          format: uint
          description: >-
            The Mode S ICAO aircraft address. Expected values are between 1 and
            16777214 decimal. The Mode S address is
             considered unique.
      description: Describes the Mode S codes.
      title: ModeS
    TransponderCodes:
      type: object
      properties:
        mode1:
          type: integer
          format: uint
          description: The mode 1 code assigned to military assets.
        mode2:
          type: integer
          format: uint
          description: The Mode 2 code assigned to military assets.
        mode3:
          type: integer
          format: uint
          description: The Mode 3 code assigned by ATC to the asset.
        mode4InterrogationResponse:
          $ref: '#/components/schemas/TransponderCodesMode4InterrogationResponse'
          description: The validity of the response from the Mode 4 interrogation.
        mode5:
          $ref: '#/components/schemas/Mode5'
          description: The Mode 5 transponder codes.
        modeS:
          $ref: '#/components/schemas/ModeS'
          description: The Mode S transponder codes.
      description: >-
        A message describing any transponder codes associated with Mode 1, 2, 3,
        4, 5, S interrogations.
      title: TransponderCodes
    ClassificationInformationLevel:
      type: string
      enum:
        - CLASSIFICATION_LEVELS_INVALID
        - CLASSIFICATION_LEVELS_UNCLASSIFIED
        - CLASSIFICATION_LEVELS_CONTROLLED_UNCLASSIFIED
        - CLASSIFICATION_LEVELS_CONFIDENTIAL
        - CLASSIFICATION_LEVELS_SECRET
        - CLASSIFICATION_LEVELS_TOP_SECRET
      description: Classification level to be applied to the information in question.
      title: ClassificationInformationLevel
    ClassificationInformation:
      type: object
      properties:
        level:
          $ref: '#/components/schemas/ClassificationInformationLevel'
          description: Classification level to be applied to the information in question.
        caveats:
          type: array
          items:
            type: string
          description: >-
            Caveats that may further restrict how the information can be
            disseminated.
      description: >-
        Represents all of the necessary information required to generate a
        summarized
         classification marking.

         > example: A summarized classification marking of "TOPSECRET//NOFORN//FISA"
                    would be defined as: { "level": 5, "caveats": [ "NOFORN, "FISA" ] }
      title: ClassificationInformation
    FieldClassificationInformation:
      type: object
      properties:
        fieldPath:
          type: string
          description: |-
            Proto field path which is the string representation of a field.
             > example: signal.bandwidth_hz would be bandwidth_hz in the signal component
        classificationInformation:
          $ref: '#/components/schemas/ClassificationInformation'
          description: >-
            The information which makes up the field level classification
            marking.
      description: A field specific classification information definition.
      title: FieldClassificationInformation
    Classification:
      type: object
      properties:
        default:
          $ref: '#/components/schemas/ClassificationInformation'
          description: >-
            The default classification information which should be assumed to
            apply to everything in
             the entity unless a specific field level classification is present.
        fields:
          type: array
          items:
            $ref: '#/components/schemas/FieldClassificationInformation'
          description: >-
            The set of individual field classification information which should
            always precedence
             over the default classification information.
      description: A component that describes an entity's security classification levels.
      title: Classification
    TaskDefinition:
      type: object
      properties:
        taskSpecificationUrl:
          type: string
          description: Url path must be prefixed with `type.googleapis.com/`.
      description: >-
        Defines a supported task by the task specification URL of its "Any"
        type.
      title: TaskDefinition
    TaskCatalog:
      type: object
      properties:
        taskDefinitions:
          type: array
          items:
            $ref: '#/components/schemas/TaskDefinition'
      description: Catalog of supported tasks.
      title: TaskCatalog
    MediaItemType:
      type: string
      enum:
        - MEDIA_TYPE_INVALID
        - MEDIA_TYPE_IMAGE
        - MEDIA_TYPE_VIDEO
      title: MediaItemType
    MediaItem:
      type: object
      properties:
        type:
          $ref: '#/components/schemas/MediaItemType'
        relativePath:
          type: string
          description: >-
            The path, relative to the environment base URL, where media related
            to an entity can be accessed
      title: MediaItem
    Media:
      type: object
      properties:
        media:
          type: array
          items:
            $ref: '#/components/schemas/MediaItem'
      description: Media associated with an entity.
      title: Media
    TrackedBy:
      type: object
      properties:
        activelyTrackingSensors:
          $ref: '#/components/schemas/Sensors'
          description: >-
            Sensor details of the tracking entity's sensors that were active and
            tracking the tracked entity. This may be
             a subset of the total sensors available on the tracking entity.
        lastMeasurementTimestamp:
          type: string
          format: date-time
          description: >-
            Latest time that any sensor in actively_tracking_sensors detected
            the tracked entity.
      description: >-
        Describes the relationship between the entity being tracked ("tracked
        entity") and the entity that is
         performing the tracking ("tracking entity").
      title: TrackedBy
    GroupChild:
      type: object
      properties: {}
      description: >-
        A GroupChild relationship is a uni-directional relationship indicating
        that (1) this entity
         represents an Entity Group and (2) the related entity is a child member of this group. The presence of this
         relationship alone determines that the type of group is an Entity Group.
      title: GroupChild
    GroupParent:
      type: object
      properties: {}
      description: >-
        A GroupParent relationship is a uni-directional relationship indicating
        that this entity is a member of
         the Entity Group represented by the related entity. The presence of this relationship alone determines that
         the type of group that this entity is a member of is an Entity Group.
      title: GroupParent
    MergedFrom:
      type: object
      properties: {}
      description: >-
        A MergedFrom relationship is a uni-directional relationship indicating
        that this entity is a merged entity whose
         data has at least partially been merged from the related entity.
      title: MergedFrom
    ActiveTarget:
      type: object
      properties: {}
      description: |-
        A target relationship is the inverse of TrackedBy; a one-way relation
         from sensor to target, indicating track(s) currently prioritized by a robot.
      title: ActiveTarget
    RelationshipType:
      type: object
      properties:
        trackedBy:
          $ref: '#/components/schemas/TrackedBy'
        groupChild:
          $ref: '#/components/schemas/GroupChild'
        groupParent:
          $ref: '#/components/schemas/GroupParent'
        mergedFrom:
          $ref: '#/components/schemas/MergedFrom'
        activeTarget:
          $ref: '#/components/schemas/ActiveTarget'
      description: Determines the type of relationship between this entity and another.
      title: RelationshipType
    Relationship:
      type: object
      properties:
        relatedEntityId:
          type: string
          description: The entity ID to which this entity is related.
        relationshipId:
          type: string
          description: >-
            A unique identifier for this relationship. Allows removing or
            updating relationships.
        relationshipType:
          $ref: '#/components/schemas/RelationshipType'
          description: The relationship type
      description: The relationship component indicates a relationship to another entity.
      title: Relationship
    Relationships:
      type: object
      properties:
        relationships:
          type: array
          items:
            $ref: '#/components/schemas/Relationship'
      description: >-
        The relationships between this entity and other entities in the common
        operational picture.
      title: Relationships
    Color:
      type: object
      properties:
        red:
          type: number
          format: double
          description: The amount of red in the color as a value in the interval [0, 1].
        green:
          type: number
          format: double
          description: The amount of green in the color as a value in the interval [0, 1].
        blue:
          type: number
          format: double
          description: The amount of blue in the color as a value in the interval [0, 1].
        alpha:
          type: number
          format: double
          description: >-
            The fraction of this color that should be applied to the pixel. That
            is,
             the final pixel color is defined by the equation:

             `pixel color = alpha * (this color) + (1.0 - alpha) * (background color)`

             This means that a value of 1.0 corresponds to a solid color, whereas
             a value of 0.0 corresponds to a completely transparent color. This
             uses a wrapper message rather than a simple float scalar so that it is
             possible to distinguish between a default value and the value being unset.
             If omitted, this color object is rendered as a solid color
             (as if the alpha value had been explicitly given a value of 1.0).
      title: Color
    RangeRings:
      type: object
      properties:
        minDistanceM:
          type: number
          format: double
          description: The minimum range ring distance, specified in meters.
        maxDistanceM:
          type: number
          format: double
          description: The maximum range ring distance, specified in meters.
        ringCount:
          type: integer
          format: uint
          description: The count of range rings.
        ringLineColor:
          $ref: '#/components/schemas/Color'
          description: The color of range rings, specified in hex string.
      description: >-
        Range rings allow visual assessment of map distance at varying zoom
        levels.
      title: RangeRings
    VisualDetails:
      type: object
      properties:
        rangeRings:
          $ref: '#/components/schemas/RangeRings'
          description: The range rings to display around an entity.
      description: Visual details associated with the display of an entity in the client.
      title: VisualDetails
    Dimensions:
      type: object
      properties:
        lengthM:
          type: number
          format: double
          description: Length of the entity in meters
      title: Dimensions
    RouteDetails:
      type: object
      properties:
        destinationName:
          type: string
          description: Free form text giving the name of the entity's destination
        estimatedArrivalTime:
          type: string
          format: date-time
          description: Estimated time of arrival at destination
      title: RouteDetails
    CronWindow:
      type: object
      properties:
        cronExpression:
          type: string
          description: >-
            in UTC, describes when and at what cadence this window starts, in
            the quartz flavor of cron

             examples:
                This schedule is begins at 7:00:00am UTC everyday between Monday and Friday
                    0 0 7 ? * MON-FRI *
                This schedule begins every 5 minutes starting at 12:00:00pm UTC until 8:00:00pm UTC everyday
                    0 0/5 12-20 * * ? *
                This schedule begins at 12:00:00pm UTC on March 2nd 2023
                    0 0 12 2 3 ? 2023
        durationMillis:
          type: string
          description: describes the duration
      title: CronWindow
    ScheduleScheduleType:
      type: string
      enum:
        - SCHEDULE_TYPE_INVALID
        - SCHEDULE_TYPE_ZONE_ENABLED
        - SCHEDULE_TYPE_ZONE_TEMP_ENABLED
      description: The schedule type
      title: ScheduleScheduleType
    Schedule:
      type: object
      properties:
        windows:
          type: array
          items:
            $ref: '#/components/schemas/CronWindow'
          description: expression that represents this schedule's "ON" state
        scheduleId:
          type: string
          description: A unique identifier for this schedule.
        scheduleType:
          $ref: '#/components/schemas/ScheduleScheduleType'
          description: The schedule type
      description: A Schedule associated with this entity
      title: Schedule
    Schedules:
      type: object
      properties:
        schedules:
          type: array
          items:
            $ref: '#/components/schemas/Schedule'
      description: Schedules associated with this entity
      title: Schedules
    HealthConnectionStatus:
      type: string
      enum:
        - CONNECTION_STATUS_INVALID
        - CONNECTION_STATUS_ONLINE
        - CONNECTION_STATUS_OFFLINE
      description: >-
        Status indicating whether the entity is able to communicate with Entity
        Manager.
      title: HealthConnectionStatus
    HealthHealthStatus:
      type: string
      enum:
        - HEALTH_STATUS_INVALID
        - HEALTH_STATUS_HEALTHY
        - HEALTH_STATUS_WARN
        - HEALTH_STATUS_FAIL
        - HEALTH_STATUS_OFFLINE
        - HEALTH_STATUS_NOT_READY
      description: >-
        Top-level health status; typically a roll-up of individual component
        healths.
      title: HealthHealthStatus
    ComponentHealthHealth:
      type: string
      enum:
        - HEALTH_STATUS_INVALID
        - HEALTH_STATUS_HEALTHY
        - HEALTH_STATUS_WARN
        - HEALTH_STATUS_FAIL
        - HEALTH_STATUS_OFFLINE
        - HEALTH_STATUS_NOT_READY
      description: Health for this component.
      title: ComponentHealthHealth
    ComponentMessageStatus:
      type: string
      enum:
        - HEALTH_STATUS_INVALID
        - HEALTH_STATUS_HEALTHY
        - HEALTH_STATUS_WARN
        - HEALTH_STATUS_FAIL
        - HEALTH_STATUS_OFFLINE
        - HEALTH_STATUS_NOT_READY
      description: The status associated with this message.
      title: ComponentMessageStatus
    ComponentMessage:
      type: object
      properties:
        status:
          $ref: '#/components/schemas/ComponentMessageStatus'
          description: The status associated with this message.
        message:
          type: string
          description: The human-readable content of the message.
      description: A message describing the component's health status.
      title: ComponentMessage
    ComponentHealth:
      type: object
      properties:
        id:
          type: string
          description: Consistent internal ID for this component.
        name:
          type: string
          description: Display name for this component.
        health:
          $ref: '#/components/schemas/ComponentHealthHealth'
          description: Health for this component.
        messages:
          type: array
          items:
            $ref: '#/components/schemas/ComponentMessage'
          description: >-
            Human-readable describing the component state. These messages should
            be understandable by end users.
        updateTime:
          type: string
          format: date-time
          description: |-
            The last update time for this specific component.
             If this timestamp is unset, the data is assumed to be most recent
      description: Health of an individual component.
      title: ComponentHealth
    AlertLevel:
      type: string
      enum:
        - ALERT_LEVEL_INVALID
        - ALERT_LEVEL_ADVISORY
        - ALERT_LEVEL_CAUTION
        - ALERT_LEVEL_WARNING
      description: Alert level (Warning, Caution, or Advisory).
      title: AlertLevel
    AlertCondition:
      type: object
      properties:
        conditionCode:
          type: string
          description: >-
            Short, machine-readable code that describes this condition. This
            code is intended to provide systems off-asset
             with a lookup key to retrieve more detailed information about the condition.
        description:
          type: string
          description: >-
            Human-readable description of this condition. The description is
            intended for display in the UI for human
             understanding and should not be used for machine processing. If the description is fixed and the vehicle controller
             provides no dynamic substitutions, then prefer lookup based on condition_code.
      description: A condition which may trigger an alert.
      title: AlertCondition
    Alert:
      type: object
      properties:
        alertCode:
          type: string
          description: >-
            Short, machine-readable code that describes this alert. This code is
            intended to provide systems off-asset
             with a lookup key to retrieve more detailed information about the alert.
        description:
          type: string
          description: >-
            Human-readable description of this alert. The description is
            intended for display in the UI for human
             understanding and should not be used for machine processing. If the description is fixed and the vehicle controller
             provides no dynamic substitutions, then prefer lookup based on alert_code.
        level:
          $ref: '#/components/schemas/AlertLevel'
          description: Alert level (Warning, Caution, or Advisory).
        activatedTime:
          type: string
          format: date-time
          description: Time at which this alert was activated.
        activeConditions:
          type: array
          items:
            $ref: '#/components/schemas/AlertCondition'
          description: Set of conditions which have activated this alert.
      description: >-
        An alert informs operators of critical events related to system
        performance and mission
         execution. An alert is produced as a result of one or more alert conditions.
      title: Alert
    Health:
      type: object
      properties:
        connectionStatus:
          $ref: '#/components/schemas/HealthConnectionStatus'
          description: >-
            Status indicating whether the entity is able to communicate with
            Entity Manager.
        healthStatus:
          $ref: '#/components/schemas/HealthHealthStatus'
          description: >-
            Top-level health status; typically a roll-up of individual component
            healths.
        components:
          type: array
          items:
            $ref: '#/components/schemas/ComponentHealth'
          description: Health of individual components running on this Entity.
        updateTime:
          type: string
          format: date-time
          description: |-
            The update time for the top-level health information.
             If this timestamp is unset, the data is assumed to be most recent
        activeAlerts:
          type: array
          items:
            $ref: '#/components/schemas/Alert'
          description: >-
            Active alerts indicate a critical change in system state sent by the
            asset
             that must be made known to an operator or consumer of the common operating picture.
             Alerts are different from ComponentHealth messages--an active alert does not necessarily
             indicate a component is in an unhealthy state. For example, an asset may trigger
             an active alert based on fuel levels running low. Alerts should be removed from this list when their conditions
             are cleared. In other words, only active alerts should be reported here.
      description: General health of the entity as reported by the entity.
      title: Health
    Agent:
      type: object
      properties:
        entityId:
          type: string
          description: Entity ID of the agent.
      description: Represents an agent capable of processing tasks.
      title: Agent
    Team:
      type: object
      properties:
        entityId:
          type: string
          description: Entity ID of the team
        members:
          type: array
          items:
            $ref: '#/components/schemas/Agent'
      description: Represents a team of agents
      title: Team
    EchelonArmyEchelon:
      type: string
      enum:
        - ARMY_ECHELON_INVALID
        - ARMY_ECHELON_FIRE_TEAM
        - ARMY_ECHELON_SQUAD
        - ARMY_ECHELON_PLATOON
        - ARMY_ECHELON_COMPANY
        - ARMY_ECHELON_BATTALION
        - ARMY_ECHELON_REGIMENT
        - ARMY_ECHELON_BRIGADE
        - ARMY_ECHELON_DIVISION
        - ARMY_ECHELON_CORPS
        - ARMY_ECHELON_ARMY
      title: EchelonArmyEchelon
    Echelon:
      type: object
      properties:
        armyEchelon:
          $ref: '#/components/schemas/EchelonArmyEchelon'
      description: >-
        Describes a Echelon group type.  Comprised of entities which are members
        of the
         same unit or echelon. Ex: A group of tanks within a armored company or that same company
         as a member of a battalion.
      title: Echelon
    GroupDetails:
      type: object
      properties:
        team:
          $ref: '#/components/schemas/Team'
        echelon:
          $ref: '#/components/schemas/Echelon'
      description: Details related to grouping for this entity
      title: GroupDetails
    Munition:
      type: object
      properties:
        munitionId:
          type: string
          description: Unique munition identifier
        name:
          type: string
          description: Long form name of the munition
        quantityUnits:
          type: integer
          format: uint
          description: Number of units
      description: Munition describes an entity's munitions stores
      title: Munition
    Fuel:
      type: object
      properties:
        fuelId:
          type: string
          description: Unique fuel identifier
        name:
          type: string
          description: Long form name of the fuel source.
        reportedDate:
          type: string
          format: date-time
          description: Timestamp the information was reported
        amountGallons:
          type: integer
          format: uint
          description: Amount of gallons on hand
        maxAuthorizedCapacityGallons:
          type: integer
          format: uint
          description: How much the asset is allowed to have available (in gallons)
        operationalRequirementGallons:
          type: integer
          format: uint
          description: Minimum required for operations (in gallons)
        dataClassification:
          $ref: '#/components/schemas/Classification'
          description: |-
            Fuel in a single asset may have different levels of classification
             Use case: fuel for a SECRET asset while diesel fuel may be UNCLASSIFIED
        dataSource:
          type: string
          description: Source of information
      description: >-
        Fuel describes an entity's repository of fuels stores including current
        amount, operational requirements, and maximum authorized capacity
      title: Fuel
    Supplies:
      type: object
      properties:
        munitions:
          type: array
          items:
            $ref: '#/components/schemas/Munition'
        fuel:
          type: array
          items:
            $ref: '#/components/schemas/Fuel'
      description: >-
        Represents the state of supplies associated with an entity (available
        but not in condition to use immediately)
      title: Supplies
    OrbitMeanElementsMetadataRefFrame:
      type: string
      enum:
        - ECI_REFERENCE_FRAME_INVALID
        - ECI_REFERENCE_FRAME_TEME
      description: Reference frame, assumed to be Earth-centered
      title: OrbitMeanElementsMetadataRefFrame
    OrbitMeanElementsMetadataMeanElementTheory:
      type: string
      enum:
        - MEAN_ELEMENT_THEORY_INVALID
        - MEAN_ELEMENT_THEORY_SGP4
      title: OrbitMeanElementsMetadataMeanElementTheory
    OrbitMeanElementsMetadata:
      type: object
      properties:
        creationDate:
          type: string
          format: date-time
          description: Creation date/time in UTC
        originator:
          type: string
          description: Creating agency or operator
        messageId:
          type: string
          description: ID that uniquely identifies a message from a given originator.
        refFrame:
          $ref: '#/components/schemas/OrbitMeanElementsMetadataRefFrame'
          description: Reference frame, assumed to be Earth-centered
        refFrameEpoch:
          type: string
          format: date-time
          description: >-
            Reference frame epoch in UTC - mandatory only if not intrinsic to
            frame definition
        meanElementTheory:
          $ref: '#/components/schemas/OrbitMeanElementsMetadataMeanElementTheory'
      title: OrbitMeanElementsMetadata
    MeanKeplerianElements:
      type: object
      properties:
        epoch:
          type: string
          format: date-time
          description: UTC time of validity
        semiMajorAxisKm:
          type: number
          format: double
          description: 'Preferred: semi major axis in kilometers'
        meanMotion:
          type: number
          format: double
          description: >-
            If using SGP/SGP4, provide the Keplerian Mean Motion in revolutions
            per day
        eccentricity:
          type: number
          format: double
        inclinationDeg:
          type: number
          format: double
          description: Angle of inclination in deg
        raOfAscNodeDeg:
          type: number
          format: double
          description: Right ascension of the ascending node in deg
        argOfPericenterDeg:
          type: number
          format: double
          description: Argument of pericenter in deg
        meanAnomalyDeg:
          type: number
          format: double
          description: Mean anomaly in deg
        gm:
          type: number
          format: double
          description: >-
            Optional: gravitational coefficient (Gravitational Constant x
            central mass) in kg^3 / s^2
      title: MeanKeplerianElements
    TleParameters:
      type: object
      properties:
        ephemerisType:
          type: integer
          format: uint
          description: Integer specifying TLE ephemeris type
        classificationType:
          type: string
          description: User-defined free-text message classification/caveats of this TLE
        noradCatId:
          type: integer
          format: uint
          description: 'Norad catalog number: integer up to nine digits.'
        elementSetNo:
          type: integer
          format: uint
        revAtEpoch:
          type: integer
          format: uint
          description: 'Optional: revolution number'
        bstar:
          type: number
          format: double
          description: Drag parameter for SGP-4 in units 1 / Earth radii
        bterm:
          type: number
          format: double
          description: Drag parameter for SGP4-XP in units m^2 / kg
        meanMotionDot:
          type: number
          format: double
          description: First time derivative of mean motion in rev / day^2
        meanMotionDdot:
          type: number
          format: double
          description: >-
            Second time derivative of mean motion in rev / day^3. For use with
            SGP or PPT3.
        agom:
          type: number
          format: double
          description: >-
            Solar radiation pressure coefficient A_gamma / m in m^2 / kg. For
            use with SGP4-XP.
      title: TleParameters
    OrbitMeanElements:
      type: object
      properties:
        metadata:
          $ref: '#/components/schemas/OrbitMeanElementsMetadata'
        meanKeplerianElements:
          $ref: '#/components/schemas/MeanKeplerianElements'
        tleParameters:
          $ref: '#/components/schemas/TleParameters'
      description: >-
        Orbit Mean Elements data, analogous to the Orbit Mean Elements Message
        in CCSDS 502.0-B-3
      title: OrbitMeanElements
    Orbit:
      type: object
      properties:
        orbitMeanElements:
          $ref: '#/components/schemas/OrbitMeanElements'
          description: >-
            Orbit Mean Elements data, analogous to the Orbit Mean Elements
            Message in CCSDS 502.0-B-3
      title: Orbit
    MilStd2525C:
      type: object
      properties:
        sidc:
          type: string
      title: MilStd2525C
    Symbology:
      type: object
      properties:
        milStd2525C:
          $ref: '#/components/schemas/MilStd2525C'
      description: Symbology associated with an entity.
      title: Symbology
